using System.Linq;
using System.Reflection;
using Mono.Cecil;

namespace ShrinkEventBus.CodeGen
{
    internal class PostProcessorReflectionImporter : DefaultReflectionImporter
    {
        private const string CoreLibName = "System.Private.CoreLib";
        private readonly AssemblyNameReference _corlib;

        public PostProcessorReflectionImporter(ModuleDefinition module) : base(module)
        {
            _corlib = module.AssemblyReferences.FirstOrDefault(
                a => a.Name is "mscorlib" or "netstandard");
        }

        public override AssemblyNameReference ImportReference(AssemblyName reference)
        {
            if (_corlib != null && reference.Name == CoreLibName)
                return _corlib;

            return base.ImportReference(reference);
        }
    }

    internal class PostProcessorReflectionImporterProvider : IReflectionImporterProvider
    {
        public IReflectionImporter GetReflectionImporter(ModuleDefinition module)
            => new PostProcessorReflectionImporter(module);
    }
}