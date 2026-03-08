using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace ShrinkEventBus.CodeGen
{
    public class EventBusILPostProcessor : ILPostProcessor
    {
        private readonly List<DiagnosticMessage> _diagnostics = new();
        private PostProcessorAssemblyResolver _assemblyResolver;
        private MethodReference _autoRegisterMethodRef;
        private MethodReference _unregisterInstanceMethodRef;

        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            return compiledAssembly.References.Any(
                r => Path.GetFileNameWithoutExtension(r) == "ShrinkEventBus.Runtime");
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            if (!WillProcess(compiledAssembly)) return null;
            _diagnostics.Clear();

            var assemblyDefinition = AssemblyDefinitionFor(compiledAssembly);
            var mainModule = assemblyDefinition.MainModule;

            ImportReferences(mainModule);

            foreach (var type in mainModule.Types)
            {
                if (!HasAttribute(type, "EventBusSubscriberAttribute")) continue;
                if (!InheritsFromMonoBehaviour(type)) continue;

                InjectAutoRegister(type, mainModule);
                InjectAutoUnregister(type, mainModule);
            }

            return GetResult(assemblyDefinition, _diagnostics);
        }

        private void InjectAutoRegister(TypeDefinition type, ModuleDefinition module)
        {
            var awake = type.Methods.FirstOrDefault(m => m.Name == "Awake" && !m.IsStatic);
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
                    il.Emit(OpCodes.Call, _autoRegisterMethodRef);
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
                processor.Create(OpCodes.Call, _autoRegisterMethodRef),
                processor.Create(OpCodes.Nop)
            };
            instructions.Reverse();
            instructions.ForEach(i => processor.Body.Instructions.Insert(0, i));
        }

        private void InjectAutoUnregister(TypeDefinition type, ModuleDefinition module)
        {
            var onDestroy = type.Methods.FirstOrDefault(m => m.Name == "OnDestroy" && !m.IsStatic);
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
                    il.Emit(OpCodes.Call, _unregisterInstanceMethodRef);
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
                processor.Create(OpCodes.Call, _unregisterInstanceMethodRef),
                processor.Create(OpCodes.Nop)
            };
            instructions.Reverse();
            instructions.ForEach(i => processor.Body.Instructions.Insert(0, i));
        }

        private static MethodReference FindBaseMethodReference(TypeDefinition type, string methodName, ModuleDefinition module)
        {
            try
            {
                var baseTypeRef = type.BaseType;
                while (baseTypeRef != null)
                {
                    var baseTypeDef = baseTypeRef.Resolve();
                    if (baseTypeDef == null) break;

                    var method = baseTypeDef.Methods.FirstOrDefault(
                        m => m.Name == methodName && !m.IsStatic && m.IsVirtual);

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
                // ignored
            }

            return null;
        }

        private void ImportReferences(ModuleDefinition module)
        {
            var runtimeModule = FindRuntimeModule(module);
            var eventBusType = runtimeModule.GetAllTypes().First(t => t.Name == "EventBus");

            _autoRegisterMethodRef = module.ImportReference(
                eventBusType.Methods.First(m => m.Name == "AutoRegister"));
            _unregisterInstanceMethodRef = module.ImportReference(
                eventBusType.Methods.First(m => m.Name == "UnregisterInstance"));
        }

        private ModuleDefinition FindRuntimeModule(ModuleDefinition module)
        {
            ModuleDefinition result = null;
            var visited = new HashSet<string>();
            SearchRecursive(_assemblyResolver.Resolve(module.Assembly.Name), ref result, visited);
            return result;
        }

        private void SearchRecursive(AssemblyDefinition asm, ref ModuleDefinition found, HashSet<string> visited)
        {
            foreach (var mod in asm.Modules)
            {
                if (mod.Name == "ShrinkEventBus.Runtime.dll")
                {
                    found = mod;
                    return;
                }
            }
            foreach (var reference in asm.MainModule.AssemblyReferences)
            {
                if (visited.Contains(reference.Name)) continue;
                visited.Add(reference.Name);
                var resolved = _assemblyResolver.Resolve(reference);
                if (resolved == null) continue;
                SearchRecursive(resolved, ref found, visited);
                if (found != null) return;
            }
        }

        private AssemblyDefinition AssemblyDefinitionFor(ICompiledAssembly compiledAssembly)
        {
            _assemblyResolver = new PostProcessorAssemblyResolver(compiledAssembly);
            var readerParameters = new ReaderParameters
            {
                SymbolStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData.ToArray()),
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                AssemblyResolver = _assemblyResolver,
                ReflectionImporterProvider = new PostProcessorReflectionImporterProvider(),
                ReadingMode = ReadingMode.Immediate
            };
            var peStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PeData.ToArray());
            var assemblyDef = AssemblyDefinition.ReadAssembly(peStream, readerParameters);
            _assemblyResolver.AddAssemblyDefinitionBeingOperatedOn(assemblyDef);
            return assemblyDef;
        }

        private static ILPostProcessResult GetResult(AssemblyDefinition asm, List<DiagnosticMessage> diagnostics)
        {
            var pe = new MemoryStream();
            var pdb = new MemoryStream();
            asm.Write(pe, new WriterParameters
            {
                SymbolWriterProvider = new PortablePdbWriterProvider(),
                SymbolStream = pdb,
                WriteSymbols = true
            });
            return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), diagnostics);
        }

        private static bool HasAttribute(TypeDefinition type, string name)
            => type.CustomAttributes.Any(a => a.AttributeType.Name == name);

        private static bool InheritsFromMonoBehaviour(TypeDefinition type)
        {
            var current = type.BaseType;
            while (current != null)
            {
                if (current.Name == "MonoBehaviour") return true;
                try { current = current.Resolve()?.BaseType; }
                catch { break; }
            }
            return false;
        }
    }
}