using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Unimetry.Editor.Weaving
{
    /// <summary>
    /// Weaves <c>[Event]</c> methods while Unity compiles assemblies.
    /// </summary>
    public sealed class EventWeaverProcessor : ILPostProcessor
    {
        private static readonly byte[] EventMarker = Encoding.UTF8.GetBytes("EventAttribute");
        private static readonly byte[] EventTagMarker = Encoding.UTF8.GetBytes("EventTagAttribute");

        public override ILPostProcessor GetInstance()
        {
            return this;
        }

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            if (compiledAssembly == null ||
                compiledAssembly.Name == "Unimetry.Runtime" ||
                compiledAssembly.Name == "Unity.Unimetry.CodeGen")
            {
                return false;
            }

            var defines = compiledAssembly.Defines;
            if (defines != null)
            {
                for (var index = 0; index < defines.Length; index++)
                {
                    if (defines[index] == "UNIMETRY_DISABLE_EVENT_WEAVE")
                    {
                        return false;
                    }
                }
            }

            var references = compiledAssembly.References;
            if (references == null)
            {
                return false;
            }

            for (var index = 0; index < references.Length; index++)
            {
                if (references[index].EndsWith("Unimetry.Runtime.dll", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var pe = compiledAssembly.InMemoryAssembly.PeData;
            var pdb = compiledAssembly.InMemoryAssembly.PdbData;
            if (!Contains(pe, EventMarker) && !Contains(pe, EventTagMarker))
            {
                return null;
            }

            var resolver = new PostProcessorAssemblyResolver(compiledAssembly);
            AssemblyDefinition assembly = null;
            MemoryStream symbolInput = null;
            try
            {
                var readerParameters = new ReaderParameters
                {
                    AssemblyResolver = resolver,
                    ReadingMode = ReadingMode.Immediate,
                    InMemory = true,
                };
                if (pdb != null && pdb.Length > 0)
                {
                    symbolInput = new MemoryStream(pdb);
                    readerParameters.ReadSymbols = true;
                    readerParameters.SymbolStream = symbolInput;
                    readerParameters.SymbolReaderProvider = new PortablePdbReaderProvider();
                }

                assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(pe), readerParameters);
                if (!EventMethodWeaver.TryCreateHooks(assembly, out var hooks, out var hookError))
                {
                    return Result(pe, pdb, Warning(hookError));
                }

                var diagnostics = new List<WeaveDiagnostic>();
                var woven = false;
                var types = new List<TypeDefinition>();
                CollectTypes(assembly.MainModule.Types, types);
                for (var typeIndex = 0; typeIndex < types.Count; typeIndex++)
                {
                    var methods = types[typeIndex].Methods;
                    for (var methodIndex = 0; methodIndex < methods.Count; methodIndex++)
                    {
                        var method = methods[methodIndex];
                        if (HasEvent(method))
                        {
                            EventMethodWeaver.Weave(method, hooks, diagnostics);
                            woven = true;
                        }
                        else
                        {
                            EventMethodWeaver.ReportOrphanTags(method, diagnostics);
                        }
                    }
                }

                if (!woven && diagnostics.Count == 0)
                {
                    return null;
                }

                if (!woven)
                {
                    return Result(pe, pdb, ToMessages(diagnostics));
                }

                var outputPe = new MemoryStream();
                var outputPdb = new MemoryStream();
                var hasSymbols = pdb != null && pdb.Length > 0;
                var writerParameters = new WriterParameters();
                if (hasSymbols)
                {
                    writerParameters.WriteSymbols = true;
                    writerParameters.SymbolStream = outputPdb;
                    writerParameters.SymbolWriterProvider = new PortablePdbWriterProvider();
                }

                assembly.Write(outputPe, writerParameters);
                return new ILPostProcessResult(
                    new InMemoryAssembly(outputPe.ToArray(), hasSymbols ? outputPdb.ToArray() : null),
                    ToMessages(diagnostics));
            }
            catch (Exception exception)
            {
                return Result(pe, pdb, Warning("Unimetry event weaver failed: " + exception.Message));
            }
            finally
            {
                assembly?.Dispose();
                symbolInput?.Dispose();
                resolver.Dispose();
            }
        }

        private static bool HasEvent(MethodDefinition method)
        {
            if (!method.HasCustomAttributes)
            {
                return false;
            }

            for (var index = 0; index < method.CustomAttributes.Count; index++)
            {
                if (method.CustomAttributes[index].AttributeType.FullName == "Unimetry.EventAttribute")
                {
                    return true;
                }
            }

            return false;
        }

        private static void CollectTypes(IEnumerable<TypeDefinition> source, List<TypeDefinition> destination)
        {
            foreach (var type in source)
            {
                destination.Add(type);
                if (type.HasNestedTypes)
                {
                    CollectTypes(type.NestedTypes, destination);
                }
            }
        }

        private static ILPostProcessResult Result(byte[] pe, byte[] pdb, List<DiagnosticMessage> diagnostics)
        {
            return new ILPostProcessResult(new InMemoryAssembly(pe, pdb), diagnostics);
        }

        private static List<DiagnosticMessage> Warning(string message)
        {
            return new List<DiagnosticMessage>
            {
                new DiagnosticMessage
                {
                    DiagnosticType = DiagnosticType.Warning,
                    MessageData = message,
                },
            };
        }

        private static List<DiagnosticMessage> ToMessages(List<WeaveDiagnostic> diagnostics)
        {
            if (diagnostics.Count == 0)
            {
                return new List<DiagnosticMessage>();
            }

            var messages = new List<DiagnosticMessage>(diagnostics.Count);
            for (var index = 0; index < diagnostics.Count; index++)
            {
                var diagnostic = diagnostics[index];
                messages.Add(new DiagnosticMessage
                {
                    DiagnosticType = DiagnosticType.Warning,
                    MessageData = diagnostic.Message,
                    File = diagnostic.File,
                    Line = diagnostic.Line,
                    Column = diagnostic.Column,
                });
            }

            return messages;
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            if (haystack == null || needle == null || haystack.Length < needle.Length)
            {
                return false;
            }

            var limit = haystack.Length - needle.Length;
            for (var index = 0; index <= limit; index++)
            {
                var match = true;
                for (var needleIndex = 0; needleIndex < needle.Length; needleIndex++)
                {
                    if (haystack[index + needleIndex] != needle[needleIndex])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
