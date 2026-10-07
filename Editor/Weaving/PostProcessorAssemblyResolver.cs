using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Unimetry.Editor.Weaving
{
    internal sealed class PostProcessorAssemblyResolver : IAssemblyResolver
    {
        private readonly string[] references;
        private readonly Dictionary<string, AssemblyDefinition> cache = new Dictionary<string, AssemblyDefinition>();

        public PostProcessorAssemblyResolver(ICompiledAssembly compiledAssembly)
        {
            references = compiledAssembly.References;
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            return Resolve(name, new ReaderParameters());
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            if (name == null)
            {
                return null;
            }

            if (cache.TryGetValue(name.Name, out var cached))
            {
                return cached;
            }

            var path = Find(name.Name);
            if (path == null)
            {
                return null;
            }

            var reader = new ReaderParameters
            {
                AssemblyResolver = this,
                ReadingMode = ReadingMode.Deferred,
                InMemory = true,
            };
            var assembly = AssemblyDefinition.ReadAssembly(path, reader);
            cache[name.Name] = assembly;
            return assembly;
        }

        public void Dispose()
        {
            foreach (var assembly in cache.Values)
            {
                assembly.Dispose();
            }

            cache.Clear();
        }

        private string Find(string name)
        {
            for (var index = 0; index < references.Length; index++)
            {
                var reference = references[index];
                if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(reference), name, StringComparison.OrdinalIgnoreCase))
                {
                    return reference;
                }
            }

            return null;
        }
    }
}
