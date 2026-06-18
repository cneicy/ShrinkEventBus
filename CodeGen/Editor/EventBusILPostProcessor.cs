#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Cecil.Pdb;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace ShrinkEventBus.CodeGen
{
    public sealed class EventBusILPostProcessor : ILPostProcessor
    {
        private const string RuntimeAssemblyName = "ShrinkEventBus.Runtime";
        private static readonly string[] SharedCoverageAssemblyNames =
        {
            "ShrinkCommand.Runtime",
            "ShrinkNetwork.Runtime",
            "ShrinkApp.Core.Runtime"
        };

        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            if (!ReferencesAssembly(compiledAssembly, RuntimeAssemblyName))
                return false;

            if (ShouldDeferToSharedCodeGen(compiledAssembly))
                return false;

            return true;
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var diagnostics = new List<DiagnosticMessage>();
            if (!WillProcess(compiledAssembly))
                return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, diagnostics);

            var assemblyDefinition = AssemblyDefinitionFor(compiledAssembly);
            var module = assemblyDefinition.MainModule;

            try
            {
                var subscriberType = FindType(module, "ShrinkEventBus.EventBusSubscriberAttribute", RuntimeAssemblyName);
                var subscribeAttributeType = FindType(module, "ShrinkEventBus.EventSubscribeAttribute", RuntimeAssemblyName);
                var eventBusType = FindType(module, "ShrinkEventBus.EventBus", RuntimeAssemblyName);
                var staticRegistryCtor = FindTypeArrayConstructor(
                    module,
                    "ShrinkEventBus.EventBusStaticRegistryAttribute",
                    RuntimeAssemblyName);
                if (subscriberType == null || subscribeAttributeType == null || eventBusType == null)
                    return GetResult(assemblyDefinition, diagnostics);

                var eventBusTypeDef = eventBusType.Resolve();
                var autoRegisterMethod = eventBusTypeDef?.Methods.FirstOrDefault(method =>
                    method.Name == "AutoRegister" &&
                    method.IsStatic &&
                    method.Parameters.Count == 1);
                var unregisterMethod = eventBusTypeDef?.Methods.FirstOrDefault(method =>
                    method.Name == "UnregisterInstance" &&
                    method.IsStatic &&
                    method.Parameters.Count == 1);
                if (autoRegisterMethod == null || unregisterMethod == null)
                    return GetResult(assemblyDefinition, diagnostics);

                var autoRegisterMethodRef = module.ImportReference(autoRegisterMethod);
                var unregisterMethodRef = module.ImportReference(unregisterMethod);

                var subscriberTypes = module.Types
                    .Where(type => HasAttribute(type, subscriberType))
                    .Where(InheritsFromMonoBehaviour)
                    .Where(type => HasInstanceSubscribeMethod(type, subscribeAttributeType))
                    .ToArray();

                foreach (var type in subscriberTypes)
                {
                    InjectAutoRegister(type, module, autoRegisterMethodRef);
                    InjectAutoUnregister(type, module, unregisterMethodRef);
                }

                if (staticRegistryCtor != null)
                {
                    var staticSubscriberTypes = module.Types
                        .Where(type => HasAttribute(type, subscriberType))
                        .Where(type => type.Methods.Any(method =>
                            method.IsStatic && HasAttribute(method, subscribeAttributeType)))
                        .Select(type => module.ImportReference(type))
                        .ToArray();
                    if (staticSubscriberTypes.Length > 0)
                        AddAssemblyTypeArrayAttribute(module, staticRegistryCtor, staticSubscriberTypes);
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add(new DiagnosticMessage
                {
                    DiagnosticType = DiagnosticType.Error,
                    MessageData = $"[ShrinkEventBus.CodeGen] {ex.Message}"
                });
            }

            return GetResult(assemblyDefinition, diagnostics);
        }

        private static bool ShouldDeferToSharedCodeGen(ICompiledAssembly compiledAssembly)
        {
            if (!IsSharedCodeGenAvailable())
                return false;

            return SharedCoverageAssemblyNames.Any(name => ReferencesAssembly(compiledAssembly, name));
        }

        private static bool IsSharedCodeGenAvailable()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(assembly.GetName().Name, "Unity.ShrinkShared.CodeGen", StringComparison.Ordinal))
                    return true;
            }

            return Type.GetType("ShrinkShared.CodeGen.ShrinkRegistryILPostProcessor, Unity.ShrinkShared.CodeGen", false) != null;
        }

        private static bool ReferencesAssembly(ICompiledAssembly compiledAssembly, string assemblyName)
        {
            return compiledAssembly.References.Any(reference =>
                string.Equals(Path.GetFileNameWithoutExtension(reference), assemblyName, StringComparison.Ordinal));
        }

        private static bool HasAttribute(ICustomAttributeProvider provider, TypeReference expectedAttributeType)
        {
            return provider.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == expectedAttributeType.FullName);
        }

        private static bool HasInstanceSubscribeMethod(TypeDefinition type, TypeReference subscribeAttributeType)
        {
            var current = type;
            while (current != null && current.Name != "MonoBehaviour")
            {
                if (current.Methods.Any(method => !method.IsStatic && HasAttribute(method, subscribeAttributeType)))
                    return true;

                var baseTypeRef = current.BaseType;
                if (baseTypeRef == null)
                    return false;

                TypeDefinition? resolvedBase;
                try
                {
                    resolvedBase = baseTypeRef.Resolve();
                }
                catch
                {
                    return true;
                }

                if (resolvedBase == null)
                    return true;

                current = resolvedBase;
            }

            return false;
        }

        private static bool InheritsFromMonoBehaviour(TypeDefinition type)
        {
            var current = type.BaseType;
            while (current != null)
            {
                if (current.Name == "MonoBehaviour")
                    return true;

                try
                {
                    current = current.Resolve()?.BaseType;
                }
                catch
                {
                    break;
                }
            }

            return false;
        }

        private static void InjectAutoRegister(TypeDefinition type, ModuleDefinition module, MethodReference autoRegisterMethodRef)
        {
            var awake = type.Methods.FirstOrDefault(method => method.Name == "Awake" && !method.IsStatic);
            if (awake == null)
            {
                var baseAwakeRef = FindBaseMethodReference(type, "Awake", module);
                if (baseAwakeRef != null)
                {
                    awake = new MethodDefinition("Awake",
                        MethodAttributes.Family | MethodAttributes.HideBySig | MethodAttributes.Virtual,
                        module.TypeSystem.Void);
                    var il = awake.Body.GetILProcessor();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Call, baseAwakeRef);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Call, autoRegisterMethodRef);
                    il.Emit(OpCodes.Ret);
                    type.Methods.Add(awake);
                    return;
                }

                awake = new MethodDefinition("Awake",
                    MethodAttributes.Private | MethodAttributes.HideBySig,
                    module.TypeSystem.Void);
                var retIl = awake.Body.GetILProcessor();
                retIl.Emit(OpCodes.Ret);
                type.Methods.Add(awake);
            }

            var processor = awake.Body.GetILProcessor();
            var instructions = new List<Instruction>
            {
                processor.Create(OpCodes.Ldarg_0),
                processor.Create(OpCodes.Call, autoRegisterMethodRef),
                processor.Create(OpCodes.Nop)
            };
            instructions.Reverse();
            instructions.ForEach(instruction => processor.Body.Instructions.Insert(0, instruction));
        }

        private static void InjectAutoUnregister(TypeDefinition type, ModuleDefinition module, MethodReference unregisterMethodRef)
        {
            var onDestroy = type.Methods.FirstOrDefault(method => method.Name == "OnDestroy" && !method.IsStatic);
            if (onDestroy == null)
            {
                var baseOnDestroyRef = FindBaseMethodReference(type, "OnDestroy", module);
                if (baseOnDestroyRef != null)
                {
                    onDestroy = new MethodDefinition("OnDestroy",
                        MethodAttributes.Family | MethodAttributes.HideBySig | MethodAttributes.Virtual,
                        module.TypeSystem.Void);
                    var il = onDestroy.Body.GetILProcessor();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Call, unregisterMethodRef);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Call, baseOnDestroyRef);
                    il.Emit(OpCodes.Ret);
                    type.Methods.Add(onDestroy);
                    return;
                }

                onDestroy = new MethodDefinition("OnDestroy",
                    MethodAttributes.Private | MethodAttributes.HideBySig,
                    module.TypeSystem.Void);
                var retIl = onDestroy.Body.GetILProcessor();
                retIl.Emit(OpCodes.Ret);
                type.Methods.Add(onDestroy);
            }

            var processor = onDestroy.Body.GetILProcessor();
            var instructions = new List<Instruction>
            {
                processor.Create(OpCodes.Ldarg_0),
                processor.Create(OpCodes.Call, unregisterMethodRef),
                processor.Create(OpCodes.Nop)
            };
            instructions.Reverse();
            instructions.ForEach(instruction => processor.Body.Instructions.Insert(0, instruction));
        }

        private static MethodReference? FindBaseMethodReference(TypeDefinition type, string methodName, ModuleDefinition module)
        {
            try
            {
                var baseTypeRef = type.BaseType;
                while (baseTypeRef != null)
                {
                    var baseTypeDef = baseTypeRef.Resolve();
                    if (baseTypeDef == null)
                        break;

                    var method = baseTypeDef.Methods.FirstOrDefault(candidate =>
                        candidate.Name == methodName && !candidate.IsStatic && candidate.IsVirtual);
                    if (method != null)
                    {
                        if (baseTypeRef is GenericInstanceType genericInstance)
                        {
                            var methodRef = new MethodReference(
                                method.Name,
                                module.ImportReference(method.ReturnType),
                                module.ImportReference(genericInstance))
                            {
                                HasThis = method.HasThis,
                                ExplicitThis = method.ExplicitThis,
                                CallingConvention = method.CallingConvention
                            };
                            return methodRef;
                        }

                        return module.ImportReference(method);
                    }

                    baseTypeRef = baseTypeDef.BaseType;
                }
            }
            catch
            {
            }

            return null;
        }

        private static TypeReference? FindType(ModuleDefinition module, string fullName, string assemblyName)
        {
            var resolved = Type.GetType($"{fullName}, {assemblyName}", false);
            return resolved == null ? null : module.ImportReference(resolved);
        }

        private static MethodReference? FindTypeArrayConstructor(ModuleDefinition module, string fullName, string assemblyName)
        {
            var typeRef = FindType(module, fullName, assemblyName);
            var typeDef = typeRef?.Resolve();
            var ctor = typeDef?.Methods.FirstOrDefault(method =>
                method.IsConstructor &&
                method.Parameters.Count == 1 &&
                method.Parameters[0].ParameterType.IsArray &&
                method.Parameters[0].ParameterType.GetElementType().FullName == module.ImportReference(typeof(Type)).FullName);
            return ctor == null ? null : module.ImportReference(ctor);
        }

        private static void AddAssemblyTypeArrayAttribute(ModuleDefinition module, MethodReference ctor, TypeReference[] types)
        {
            var attribute = new CustomAttribute(ctor);
            var typeTypeRef = module.ImportReference(typeof(Type));
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(
                module.ImportReference(typeof(Type[])),
                types.Select(type => new CustomAttributeArgument(typeTypeRef, type)).ToArray()));
            module.Assembly.CustomAttributes.Add(attribute);
        }

        private static AssemblyDefinition AssemblyDefinitionFor(ICompiledAssembly compiledAssembly)
        {
            var assemblyResolver = new PostProcessorAssemblyResolver(compiledAssembly);
            var readerParameters = new ReaderParameters
            {
                SymbolStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData.ToArray()),
                SymbolReaderProvider = new PdbReaderProvider(),
                AssemblyResolver = assemblyResolver,
                ReflectionImporterProvider = new PostProcessorReflectionImporterProvider(),
                ReadingMode = ReadingMode.Immediate
            };

            var assemblyDefinition = AssemblyDefinition.ReadAssembly(
                new MemoryStream(compiledAssembly.InMemoryAssembly.PeData.ToArray()),
                readerParameters);
            assemblyResolver.AddAssemblyDefinitionBeingOperatedOn(assemblyDefinition);
            return assemblyDefinition;
        }

        private static ILPostProcessResult GetResult(AssemblyDefinition assemblyDefinition, List<DiagnosticMessage> diagnostics)
        {
            var pe = new MemoryStream();
            var pdb = new MemoryStream();
            assemblyDefinition.Write(pe, new WriterParameters
            {
                SymbolWriterProvider = new PdbWriterProvider(),
                SymbolStream = pdb,
                WriteSymbols = true
            });
            return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), diagnostics);
        }
    }
}
