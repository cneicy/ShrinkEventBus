#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Threading;
using Mono.Cecil;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace ShrinkEventBus.CodeGen
{
    internal sealed class PostProcessorAssemblyResolver : IAssemblyResolver
    {
        private readonly string[] _references;
        private readonly Dictionary<string, AssemblyDefinition> _cache = new();
        private AssemblyDefinition? _self;

        public PostProcessorAssemblyResolver(ICompiledAssembly compiledAssembly)
        {
            _references = compiledAssembly.References;
        }

        public void AddAssemblyDefinitionBeingOperatedOn(AssemblyDefinition assemblyDefinition)
        {
            _self = assemblyDefinition;
        }

        public AssemblyDefinition? Resolve(AssemblyNameReference name)
        {
            return Resolve(name, new ReaderParameters(ReadingMode.Deferred));
        }

        public AssemblyDefinition? Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            lock (_cache)
            {
                if (name.Name == _self?.Name.Name)
                    return _self;

                var path = FindPath(name);
                if (path == null)
                    return null;

                var key = $"{path}{File.GetLastWriteTime(path)}";
                if (_cache.TryGetValue(key, out var cached))
                    return cached;

                parameters.AssemblyResolver = this;
                var ms = ReadFileWithRetry(path);

                var pdbPath = Path.ChangeExtension(path, ".pdb");
                if (File.Exists(pdbPath))
                    parameters.SymbolStream = ReadFileWithRetry(pdbPath);

                var assembly = AssemblyDefinition.ReadAssembly(ms, parameters);
                _cache[key] = assembly;
                return assembly;
            }
        }

        private string? FindPath(AssemblyNameReference name)
        {
            foreach (var reference in _references)
            {
                if (Path.GetFileNameWithoutExtension(reference) == name.Name)
                    return reference;
            }

            var dirs = new HashSet<string>();
            foreach (var reference in _references)
            {
                var dir = Path.GetDirectoryName(reference);
                if (dir != null)
                    dirs.Add(dir);
            }

            foreach (var dir in dirs)
            {
                var candidate = Path.Combine(dir, $"{name.Name}.dll");
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static MemoryStream ReadFileWithRetry(string path, int retries = 5)
        {
            for (var i = 0; i < retries; i++)
            {
                try
                {
                    return new MemoryStream(File.ReadAllBytes(path));
                }
                catch (IOException) when (i < retries - 1)
                {
                    Thread.Sleep(100);
                }
            }

            throw new IOException($"[ShrinkEventBus.CodeGen] 无法读取文件: {path}");
        }

        public void Dispose()
        {
        }
    }
}
