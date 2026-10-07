using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Unimetry.Editor.Weaving
{
    internal readonly struct WeaveDiagnostic
    {
        public WeaveDiagnostic(string message, string file, int line, int column)
        {
            Message = message;
            File = file;
            Line = line;
            Column = column;
        }

        public string Message { get; }

        public string File { get; }

        public int Line { get; }

        public int Column { get; }
    }

    internal sealed class WeaveHooks
    {
        public FieldReference EventEnabled;
        public MethodReference WeaveStart;
        public MethodReference SetTagString;
        public MethodReference SetTagBool;
        public MethodReference SetTagInt;
        public MethodReference SetTagLong;
        public MethodReference SetTagDouble;
        public MethodReference RecordException;
        public MethodReference WeaveStop;
        public MethodReference WovenConstructor;
        public TypeReference ExceptionType;
        public TypeReference EventHandleType;
    }

    internal static class EventMethodWeaver
    {
        private const string HandleFieldName = "<Unimetry>event";

        public static bool TryCreateHooks(AssemblyDefinition assembly, out WeaveHooks hooks, out string error)
        {
            hooks = null;
            error = null;
            var runtime = ResolveAssembly(assembly, "Unimetry.Runtime");
            if (runtime == null)
            {
                error = "Unimetry.Runtime could not be resolved.";
                return false;
            }

            var eventType = runtime.MainModule.GetType("Unimetry.UnimetryEvent");
            var handleType = runtime.MainModule.GetType("Unimetry.EventHandle");
            var wovenType = runtime.MainModule.GetType("Unimetry.EventWovenAttribute");
            var exceptionType = FindType(assembly, "System.Exception");
            if (eventType == null || handleType == null || wovenType == null || exceptionType == null)
            {
                error = "Unimetry event weave hooks were not found.";
                return false;
            }

            var module = assembly.MainModule;
            hooks = new WeaveHooks
            {
                EventEnabled = module.ImportReference(FindField(eventType, "EventEnabled")),
                WeaveStart = module.ImportReference(FindMethod(eventType, "WeaveStart", "System.String")),
                SetTagString = module.ImportReference(FindMethod(eventType, "WeaveSetTag", "Unimetry.EventHandle", "System.String", "System.String")),
                SetTagBool = module.ImportReference(FindMethod(eventType, "WeaveSetTag", "Unimetry.EventHandle", "System.String", "System.Boolean")),
                SetTagInt = module.ImportReference(FindMethod(eventType, "WeaveSetTag", "Unimetry.EventHandle", "System.String", "System.Int32")),
                SetTagLong = module.ImportReference(FindMethod(eventType, "WeaveSetTag", "Unimetry.EventHandle", "System.String", "System.Int64")),
                SetTagDouble = module.ImportReference(FindMethod(eventType, "WeaveSetTag", "Unimetry.EventHandle", "System.String", "System.Double")),
                RecordException = module.ImportReference(FindMethod(eventType, "WeaveRecordException", "Unimetry.EventHandle", "System.Exception")),
                WeaveStop = module.ImportReference(FindMethod(eventType, "WeaveStop", "Unimetry.EventHandle")),
                WovenConstructor = module.ImportReference(FindConstructor(wovenType)),
                ExceptionType = module.ImportReference(exceptionType),
                EventHandleType = module.ImportReference(handleType),
            };

            if (hooks.EventEnabled == null ||
                hooks.WeaveStart == null ||
                hooks.SetTagString == null ||
                hooks.SetTagBool == null ||
                hooks.SetTagInt == null ||
                hooks.SetTagLong == null ||
                hooks.SetTagDouble == null ||
                hooks.RecordException == null ||
                hooks.WeaveStop == null ||
                hooks.WovenConstructor == null)
            {
                error = "Unimetry event weave hooks were incomplete.";
                hooks = null;
                return false;
            }

            return true;
        }

        public static void Weave(MethodDefinition method, WeaveHooks hooks, List<WeaveDiagnostic> diagnostics)
        {
            if (HasAttribute(method, "Unimetry.EventWovenAttribute"))
            {
                return;
            }

            if (!method.HasBody || method.IsAbstract || method.IsPInvokeImpl || method.IsInternalCall)
            {
                diagnostics.Add(Create(method, "UM004: " + Display(method) + " has no body and cannot be woven."));
                return;
            }

            if (method.Name.Contains("<") || (method.DeclaringType.Name.Contains("<") && !method.DeclaringType.Name.Contains("d__")))
            {
                diagnostics.Add(Create(method, "UM004: " + Display(method) + " is a local function and cannot be woven."));
                return;
            }

            if (method.HasGenericParameters)
            {
                diagnostics.Add(Create(method, "UM004: " + Display(method) + " is generic and cannot be woven."));
                return;
            }

            if (method.ReturnType.IsByReference)
            {
                diagnostics.Add(Create(method, "UM004: " + Display(method) + " returns by reference and cannot be woven."));
                return;
            }

            if (HasAttribute(method, "System.Runtime.CompilerServices.IteratorStateMachineAttribute"))
            {
                diagnostics.Add(Create(method, "UM004: " + Display(method) + " is an iterator and cannot be woven."));
                return;
            }

            var asyncAttribute = GetAttribute(method, "System.Runtime.CompilerServices.AsyncStateMachineAttribute");
            if (asyncAttribute != null && method.ReturnType.MetadataType == MetadataType.Void)
            {
                diagnostics.Add(Create(method, "UM004: " + Display(method) + " is async void and cannot be woven."));
                return;
            }

            if (!TryGetEventName(method, out var eventName, out var nameDiagnostic))
            {
                diagnostics.Add(nameDiagnostic);
                return;
            }

            if (!TryGetTags(method, hooks, diagnostics, out var tags))
            {
                return;
            }

            if (asyncAttribute != null)
            {
                WeaveAsync(method, hooks, eventName, tags, diagnostics);
                return;
            }

            WeaveSync(method, hooks, eventName, tags);
        }

        public static void ReportOrphanTags(MethodDefinition method, List<WeaveDiagnostic> diagnostics)
        {
            if (HasAttribute(method, "Unimetry.EventAttribute") || !method.HasParameters)
            {
                return;
            }

            for (var index = 0; index < method.Parameters.Count; index++)
            {
                if (HasAttribute(method.Parameters[index], "Unimetry.EventTagAttribute"))
                {
                    diagnostics.Add(Create(method, "UM005: " + Display(method) + " has EventTag without Event."));
                    return;
                }
            }
        }

        private static void WeaveSync(MethodDefinition method, WeaveHooks hooks, string eventName, List<TagSite> tags)
        {
            var body = method.Body;
            ExpandBranches(body);
            var originalInstructions = body.Instructions.ToArray();
            var handlerSnapshots = SnapshotHandlers(body);
            var fastPath = CloneInstructions(originalInstructions);
            var il = body.GetILProcessor();
            body.InitLocals = true;

            var handle = new VariableDefinition(hooks.EventHandleType);
            var exception = new VariableDefinition(hooks.ExceptionType);
            body.Variables.Add(handle);
            body.Variables.Add(exception);
            VariableDefinition result = null;
            if (method.ReturnType.MetadataType != MetadataType.Void)
            {
                result = new VariableDefinition(method.ReturnType);
                body.Variables.Add(result);
            }

            var afterHandlers = il.Create(OpCodes.Nop);
            foreach (var instruction in body.Instructions.ToArray())
            {
                if (instruction.OpCode != OpCodes.Ret)
                {
                    continue;
                }

                if (instruction.Previous != null && instruction.Previous.OpCode == OpCodes.Tail)
                {
                    il.Remove(instruction.Previous);
                }

                if (result != null)
                {
                    var store = il.Create(OpCodes.Stloc, result);
                    il.InsertBefore(instruction, store);
                }

                var leave = il.Create(OpCodes.Leave, afterHandlers);
                Retarget(body, instruction, leave);
                il.Replace(instruction, leave);
            }

            var anchor = body.Instructions[0];
            var prologue = new List<Instruction>
            {
                il.Create(OpCodes.Ldstr, eventName),
                il.Create(OpCodes.Call, hooks.WeaveStart),
                il.Create(OpCodes.Stloc, handle),
            };
            AppendTags(il, prologue, tags, handle);
            InsertBefore(il, anchor, prologue);

            var catchStart = il.Create(OpCodes.Nop);
            var rethrow = il.Create(OpCodes.Rethrow);
            var finallyStart = il.Create(OpCodes.Nop);
            var endFinally = il.Create(OpCodes.Endfinally);
            var skipDispose = il.Create(OpCodes.Nop);
            var skipRecord = il.Create(OpCodes.Nop);

            il.Append(catchStart);
            CloseOpenHandlers(body, catchStart);
            il.Append(il.Create(OpCodes.Stloc, exception));
            il.Append(il.Create(OpCodes.Ldloc, handle));
            il.Append(il.Create(OpCodes.Brfalse, skipRecord));
            il.Append(il.Create(OpCodes.Ldloc, handle));
            il.Append(il.Create(OpCodes.Ldloc, exception));
            il.Append(il.Create(OpCodes.Call, hooks.RecordException));
            il.Append(skipRecord);
            il.Append(rethrow);

            il.Append(finallyStart);
            il.Append(il.Create(OpCodes.Ldloc, handle));
            il.Append(il.Create(OpCodes.Brfalse, skipDispose));
            il.Append(il.Create(OpCodes.Ldloc, handle));
            il.Append(il.Create(OpCodes.Call, hooks.WeaveStop));
            il.Append(skipDispose);
            il.Append(endFinally);

            il.Append(afterHandlers);
            if (result != null)
            {
                il.Append(il.Create(OpCodes.Ldloc, result));
            }

            il.Append(il.Create(OpCodes.Ret));

            var tryStart = prologue[0];
            body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
            {
                TryStart = tryStart,
                TryEnd = catchStart,
                HandlerStart = catchStart,
                HandlerEnd = finallyStart,
                CatchType = hooks.ExceptionType,
            });
            body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
            {
                TryStart = tryStart,
                TryEnd = finallyStart,
                HandlerStart = finallyStart,
                HandlerEnd = afterHandlers,
            });

            var slowStart = prologue[0];
            var fastPathInstructions = new List<Instruction>
            {
                il.Create(OpCodes.Volatile),
                il.Create(OpCodes.Ldsfld, hooks.EventEnabled),
                il.Create(OpCodes.Brtrue, slowStart),
            };
            for (var index = 0; index < originalInstructions.Length; index++)
            {
                fastPathInstructions.Add(fastPath[originalInstructions[index]]);
            }

            InsertBefore(il, slowStart, fastPathInstructions);
            AddFastPathHandlers(body, handlerSnapshots, fastPath, slowStart);
            body.MaxStackSize = Math.Max(body.MaxStackSize, 8) + 4;
            method.CustomAttributes.Add(new CustomAttribute(hooks.WovenConstructor));
        }

        private static void WeaveAsync(
            MethodDefinition method,
            WeaveHooks hooks,
            string eventName,
            List<TagSite> tags,
            List<WeaveDiagnostic> diagnostics)
        {
            var attribute = GetAttribute(method, "System.Runtime.CompilerServices.AsyncStateMachineAttribute");
            if (attribute == null ||
                attribute.ConstructorArguments.Count != 1 ||
                !(attribute.ConstructorArguments[0].Value is TypeReference stateMachineType))
            {
                diagnostics.Add(Create(method, "UM004: " + Display(method) + " async state machine could not be resolved."));
                return;
            }

            var stateMachine = stateMachineType.Resolve();
            MethodDefinition moveNext = null;
            FieldDefinition builderField = null;
            if (stateMachine != null)
            {
                for (var index = 0; index < stateMachine.Methods.Count; index++)
                {
                    var candidate = stateMachine.Methods[index];
                    if (candidate.Name == "MoveNext" && candidate.HasBody)
                    {
                        moveNext = candidate;
                        break;
                    }
                }

                for (var index = 0; index < stateMachine.Fields.Count; index++)
                {
                    var candidate = stateMachine.Fields[index];
                    if (candidate.Name == "<>t__builder")
                    {
                        builderField = candidate;
                        break;
                    }
                }
            }

            if (stateMachine == null || moveNext == null || builderField == null || !IsSupportedBuilder(builderField.FieldType))
            {
                diagnostics.Add(Create(method, "UM004: " + Display(method) + " async method builder is not supported."));
                return;
            }

            ExpandBranches(method.Body);
            ExpandBranches(moveNext.Body);
            var handleField = new FieldDefinition(HandleFieldName, FieldAttributes.Assembly, hooks.EventHandleType);
            stateMachine.Fields.Add(handleField);
            if (!TryPrepareAsync(method, moveNext, stateMachine, hooks, eventName, tags, handleField, diagnostics))
            {
                return;
            }

            if (!TryWeaveMoveNext(method, moveNext, hooks, handleField, builderField, diagnostics))
            {
                return;
            }

            method.CustomAttributes.Add(new CustomAttribute(hooks.WovenConstructor));
        }

        private static bool TryPrepareAsync(
            MethodDefinition method,
            MethodDefinition moveNext,
            TypeDefinition stateMachine,
            WeaveHooks hooks,
            string eventName,
            List<TagSite> tags,
            FieldDefinition handleField,
            List<WeaveDiagnostic> diagnostics)
        {
            FieldDefinition stateField = null;
            for (var index = 0; index < stateMachine.Fields.Count; index++)
            {
                if (stateMachine.Fields[index].Name == "<>1__state")
                {
                    stateField = stateMachine.Fields[index];
                    break;
                }
            }

            if (stateField == null)
            {
                diagnostics.Add(Create(method, "UM004: " + Display(method) + " async state field was not recognized."));
                return false;
            }

            var boundTags = new List<BoundTag>(tags.Count);
            for (var index = 0; index < tags.Count; index++)
            {
                var tag = tags[index];
                FieldDefinition field = null;
                for (var fieldIndex = 0; fieldIndex < stateMachine.Fields.Count; fieldIndex++)
                {
                    var candidate = stateMachine.Fields[fieldIndex];
                    if (candidate.Name == tag.Parameter.Name &&
                        candidate.FieldType.FullName == tag.Parameter.ParameterType.FullName)
                    {
                        field = candidate;
                        break;
                    }
                }

                var storeInStub = false;
                if (field == null)
                {
                    field = new FieldDefinition("<Unimetry>tag_" + tag.Parameter.Name, FieldAttributes.Assembly, tag.Parameter.ParameterType);
                    stateMachine.Fields.Add(field);
                    storeInStub = true;
                }

                boundTags.Add(new BoundTag(tag, field, storeInStub));
            }

            if (!TryStoreTagFields(method, boundTags, diagnostics))
            {
                return false;
            }

            var outerTry = EarliestTryStart(moveNext.Body);
            if (outerTry == null)
            {
                diagnostics.Add(Create(method, "UM004: " + Display(method) + " async state machine was not recognized."));
                return false;
            }

            var il = moveNext.Body.GetILProcessor();
            var skip = il.Create(OpCodes.Nop);
            var skipTags = il.Create(OpCodes.Nop);
            var block = new List<Instruction>
            {
                il.Create(OpCodes.Ldarg_0),
                il.Create(OpCodes.Ldfld, stateField),
                il.Create(OpCodes.Ldc_I4_M1),
                il.Create(OpCodes.Bne_Un, skip),
                il.Create(OpCodes.Volatile),
                il.Create(OpCodes.Ldsfld, hooks.EventEnabled),
                il.Create(OpCodes.Brfalse, skip),
                il.Create(OpCodes.Ldarg_0),
                il.Create(OpCodes.Ldstr, eventName),
                il.Create(OpCodes.Call, hooks.WeaveStart),
                il.Create(OpCodes.Stfld, handleField),
                il.Create(OpCodes.Ldarg_0),
                il.Create(OpCodes.Ldfld, handleField),
                il.Create(OpCodes.Brfalse, skipTags),
            };

            for (var index = 0; index < boundTags.Count; index++)
            {
                var tag = boundTags[index];
                block.Add(il.Create(OpCodes.Ldarg_0));
                block.Add(il.Create(OpCodes.Ldfld, handleField));
                block.Add(il.Create(OpCodes.Ldstr, tag.Site.Name));
                block.Add(il.Create(OpCodes.Ldarg_0));
                block.Add(il.Create(OpCodes.Ldfld, tag.Field));
                block.Add(il.Create(OpCodes.Call, tag.Site.Setter));
            }

            block.Add(skipTags);
            block.Add(skip);
            InsertBefore(il, outerTry, block);
            for (var index = 0; index < moveNext.Body.ExceptionHandlers.Count; index++)
            {
                var handler = moveNext.Body.ExceptionHandlers[index];
                if (handler.TryStart == outerTry)
                {
                    handler.TryStart = block[0];
                }
            }

            moveNext.Body.MaxStackSize = Math.Max(moveNext.Body.MaxStackSize, 8) + 4;
            return true;
        }

        private static bool TryStoreTagFields(MethodDefinition method, List<BoundTag> tags, List<WeaveDiagnostic> diagnostics)
        {
            var needsStore = false;
            for (var index = 0; index < tags.Count; index++)
            {
                if (tags[index].StoreInStub)
                {
                    needsStore = true;
                    break;
                }
            }

            if (!needsStore)
            {
                return true;
            }

            Instruction startCall = null;
            for (var index = 0; index < method.Body.Instructions.Count; index++)
            {
                var instruction = method.Body.Instructions[index];
                if (instruction.OpCode == OpCodes.Call &&
                    instruction.Operand is MethodReference called &&
                    called.Name == "Start" &&
                    IsSupportedBuilder(called.DeclaringType))
                {
                    startCall = instruction;
                    break;
                }
            }

            if (startCall == null ||
                startCall.Previous == null ||
                !(startCall.Previous.Operand is VariableDefinition stateMachineVariable) ||
                (startCall.Previous.OpCode != OpCodes.Ldloca && startCall.Previous.OpCode != OpCodes.Ldloca_S) ||
                !EventIl.TryFindSequenceStart(startCall, out var sequenceStart))
            {
                diagnostics.Add(Create(method, "UM004: " + Display(method) + " async state machine startup was not recognized."));
                return false;
            }

            var il = method.Body.GetILProcessor();
            var stores = new List<Instruction>();
            for (var index = 0; index < tags.Count; index++)
            {
                var tag = tags[index];
                if (!tag.StoreInStub)
                {
                    continue;
                }

                var field = method.Module.ImportReference(
                    new FieldReference(tag.Field.Name, tag.Field.FieldType, stateMachineVariable.VariableType));
            if (IsValueType(stateMachineVariable.VariableType))
            {
                stores.Add(il.Create(OpCodes.Ldloca, stateMachineVariable));
            }
            else
            {
                stores.Add(il.Create(OpCodes.Ldloc, stateMachineVariable));
            }

                stores.Add(il.Create(OpCodes.Ldarg, tag.Site.Parameter));
                stores.Add(il.Create(OpCodes.Stfld, field));
            }

            InsertBefore(il, sequenceStart, stores);
            return true;
        }

        private static bool TryWeaveMoveNext(
            MethodDefinition source,
            MethodDefinition moveNext,
            WeaveHooks hooks,
            FieldDefinition handleField,
            FieldDefinition builderField,
            List<WeaveDiagnostic> diagnostics)
        {
            var completions = new List<KeyValuePair<Instruction, VariableDefinition>>();
            for (var index = 0; index < moveNext.Body.Instructions.Count; index++)
            {
                var instruction = moveNext.Body.Instructions[index];
                var called = instruction.Operand as MethodReference;
                if (called == null ||
                    (instruction.OpCode.Code != Code.Call && instruction.OpCode.Code != Code.Callvirt) ||
                    (called.Name != "SetResult" && called.Name != "SetException") ||
                    !SameType(called.DeclaringType, builderField.FieldType))
                {
                    continue;
                }

                if (called.Name == "SetException")
                {
                    var previous = instruction.Previous;
                    if (previous == null || !EventIl.TryGetLoadedVariable(moveNext.Body, previous, out var exception))
                    {
                        diagnostics.Add(Create(source, "UM004: " + Display(source) + " async exception path was not recognized."));
                        return false;
                    }

                    completions.Add(new KeyValuePair<Instruction, VariableDefinition>(instruction, exception));
                }
                else
                {
                    completions.Add(new KeyValuePair<Instruction, VariableDefinition>(instruction, null));
                }
            }

            if (completions.Count == 0)
            {
                diagnostics.Add(Create(source, "UM004: " + Display(source) + " async completion path was not recognized."));
                return false;
            }

            var il = moveNext.Body.GetILProcessor();
            for (var index = 0; index < completions.Count; index++)
            {
                var call = completions[index].Key;
                var exception = completions[index].Value;
                if (!EventIl.TryFindSequenceStart(call, out var sequenceStart))
                {
                    diagnostics.Add(Create(source, "UM004: " + Display(source) + " async completion path was not recognized."));
                    return false;
                }

                InsertBefore(il, sequenceStart, EmitStop(il, handleField, hooks, exception));
            }

            moveNext.Body.MaxStackSize = Math.Max(moveNext.Body.MaxStackSize, 8) + 4;
            return true;
        }

        private static List<Instruction> EmitStop(
            ILProcessor il,
            FieldDefinition handleField,
            WeaveHooks hooks,
            VariableDefinition exception)
        {
            var skip = il.Create(OpCodes.Nop);
            var instructions = new List<Instruction>
            {
                il.Create(OpCodes.Ldarg_0),
                il.Create(OpCodes.Ldfld, handleField),
                il.Create(OpCodes.Brfalse, skip),
            };

            if (exception != null)
            {
                instructions.Add(il.Create(OpCodes.Ldarg_0));
                instructions.Add(il.Create(OpCodes.Ldfld, handleField));
                instructions.Add(il.Create(OpCodes.Ldloc, exception));
                instructions.Add(il.Create(OpCodes.Call, hooks.RecordException));
            }

            instructions.Add(il.Create(OpCodes.Ldarg_0));
            instructions.Add(il.Create(OpCodes.Ldfld, handleField));
            instructions.Add(il.Create(OpCodes.Call, hooks.WeaveStop));
            instructions.Add(skip);
            return instructions;
        }

        private static void AppendTags(ILProcessor il, List<Instruction> instructions, List<TagSite> tags, VariableDefinition handle)
        {
            if (tags.Count == 0)
            {
                return;
            }

            var skip = il.Create(OpCodes.Nop);
            instructions.Add(il.Create(OpCodes.Ldloc, handle));
            instructions.Add(il.Create(OpCodes.Brfalse, skip));
            for (var index = 0; index < tags.Count; index++)
            {
                var tag = tags[index];
                instructions.Add(il.Create(OpCodes.Ldloc, handle));
                instructions.Add(il.Create(OpCodes.Ldstr, tag.Name));
                instructions.Add(il.Create(OpCodes.Ldarg, tag.Parameter));
                instructions.Add(il.Create(OpCodes.Call, tag.Setter));
            }

            instructions.Add(skip);
        }

        private static bool TryGetEventName(MethodDefinition method, out string eventName, out WeaveDiagnostic diagnostic)
        {
            var attribute = GetAttribute(method, "Unimetry.EventAttribute");
            if (attribute != null &&
                attribute.ConstructorArguments.Count == 1 &&
                attribute.ConstructorArguments[0].Value is string explicitName)
            {
                if (string.IsNullOrWhiteSpace(explicitName))
                {
                    eventName = string.Empty;
                    diagnostic = Create(method, "UM003: event name must not be empty.");
                    return false;
                }

                eventName = explicitName;
                diagnostic = default;
                return true;
            }

            eventName = Display(method);
            diagnostic = default;
            return true;
        }

        private static bool TryGetTags(
            MethodDefinition method,
            WeaveHooks hooks,
            List<WeaveDiagnostic> diagnostics,
            out List<TagSite> tags)
        {
            tags = new List<TagSite>();
            var ok = true;
            for (var index = 0; index < method.Parameters.Count; index++)
            {
                var parameter = method.Parameters[index];
                var attribute = GetAttribute(parameter, "Unimetry.EventTagAttribute");
                if (attribute == null)
                {
                    continue;
                }

                string explicitName = null;
                if (attribute.ConstructorArguments.Count == 1 && attribute.ConstructorArguments[0].Value is string value)
                {
                    explicitName = value;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        diagnostics.Add(Create(method, "UM003: event tag name must not be empty."));
                        ok = false;
                        continue;
                    }
                }

                var name = explicitName ?? parameter.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    diagnostics.Add(Create(method, "UM003: event tag name must not be empty."));
                    ok = false;
                    continue;
                }

                if (parameter.ParameterType.IsByReference || !TryGetSetter(hooks, parameter.ParameterType, out var setter))
                {
                    diagnostics.Add(Create(method, "UM001: event tag '" + name + "' has unsupported type " + parameter.ParameterType.FullName + "."));
                    ok = false;
                    continue;
                }

                tags.Add(new TagSite(name, parameter, setter));
            }

            return ok;
        }

        private static bool TryGetSetter(WeaveHooks hooks, TypeReference type, out MethodReference setter)
        {
            switch (type.FullName)
            {
                case "System.String":
                    setter = hooks.SetTagString;
                    return true;
                case "System.Boolean":
                    setter = hooks.SetTagBool;
                    return true;
                case "System.Int32":
                    setter = hooks.SetTagInt;
                    return true;
                case "System.Int64":
                    setter = hooks.SetTagLong;
                    return true;
                case "System.Double":
                    setter = hooks.SetTagDouble;
                    return true;
                default:
                    setter = null;
                    return false;
            }
        }

        private static bool IsValueType(TypeReference type)
        {
            if (type == null)
            {
                return false;
            }

            if (type.IsValueType)
            {
                return true;
            }

            try
            {
                var resolved = type.Resolve();
                return resolved != null && resolved.IsValueType;
            }
            catch (AssemblyResolutionException)
            {
                return false;
            }
        }

        private static bool IsSupportedBuilder(TypeReference type)
        {
            var name = ElementName(type);
            return name == "System.Runtime.CompilerServices.AsyncTaskMethodBuilder" ||
                   name == "System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1" ||
                   name == "System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder" ||
                   name == "System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder`1";
        }

        private static bool SameType(TypeReference left, TypeReference right)
        {
            return ElementName(left) == ElementName(right);
        }

        private static string ElementName(TypeReference type)
        {
            var generic = type as GenericInstanceType;
            return generic != null ? generic.ElementType.FullName : type.FullName;
        }

        private static Instruction EarliestTryStart(MethodBody body)
        {
            Instruction earliest = null;
            var earliestIndex = int.MaxValue;
            for (var index = 0; index < body.ExceptionHandlers.Count; index++)
            {
                var handler = body.ExceptionHandlers[index];
                if (handler.HandlerType != ExceptionHandlerType.Catch || handler.TryStart == null)
                {
                    continue;
                }

                var instructionIndex = body.Instructions.IndexOf(handler.TryStart);
                if (instructionIndex >= 0 && instructionIndex < earliestIndex)
                {
                    earliestIndex = instructionIndex;
                    earliest = handler.TryStart;
                }
            }

            return earliest;
        }

        private static void CloseOpenHandlers(MethodBody body, Instruction boundary)
        {
            for (var index = 0; index < body.ExceptionHandlers.Count; index++)
            {
                var handler = body.ExceptionHandlers[index];
                if (handler.TryEnd == null)
                {
                    handler.TryEnd = boundary;
                }

                if (handler.HandlerEnd == null)
                {
                    handler.HandlerEnd = boundary;
                }
            }
        }

        private static void ExpandBranches(MethodBody body)
        {
            for (var index = 0; index < body.Instructions.Count; index++)
            {
                var instruction = body.Instructions[index];
                var code = instruction.OpCode.Code;
                if (code == Code.Br_S)
                {
                    instruction.OpCode = OpCodes.Br;
                }
                else if (code == Code.Brfalse_S)
                {
                    instruction.OpCode = OpCodes.Brfalse;
                }
                else if (code == Code.Brtrue_S)
                {
                    instruction.OpCode = OpCodes.Brtrue;
                }
                else if (code == Code.Beq_S)
                {
                    instruction.OpCode = OpCodes.Beq;
                }
                else if (code == Code.Bge_S)
                {
                    instruction.OpCode = OpCodes.Bge;
                }
                else if (code == Code.Bgt_S)
                {
                    instruction.OpCode = OpCodes.Bgt;
                }
                else if (code == Code.Ble_S)
                {
                    instruction.OpCode = OpCodes.Ble;
                }
                else if (code == Code.Blt_S)
                {
                    instruction.OpCode = OpCodes.Blt;
                }
                else if (code == Code.Bne_Un_S)
                {
                    instruction.OpCode = OpCodes.Bne_Un;
                }
                else if (code == Code.Bge_Un_S)
                {
                    instruction.OpCode = OpCodes.Bge_Un;
                }
                else if (code == Code.Bgt_Un_S)
                {
                    instruction.OpCode = OpCodes.Bgt_Un;
                }
                else if (code == Code.Ble_Un_S)
                {
                    instruction.OpCode = OpCodes.Ble_Un;
                }
                else if (code == Code.Blt_Un_S)
                {
                    instruction.OpCode = OpCodes.Blt_Un;
                }
                else if (code == Code.Leave_S)
                {
                    instruction.OpCode = OpCodes.Leave;
                }
            }
        }

        private static void Retarget(MethodBody body, Instruction from, Instruction to)
        {
            for (var index = 0; index < body.Instructions.Count; index++)
            {
                var instruction = body.Instructions[index];
                if (instruction.Operand == from)
                {
                    instruction.Operand = to;
                }
                else if (instruction.Operand is Instruction[] targets)
                {
                    for (var targetIndex = 0; targetIndex < targets.Length; targetIndex++)
                    {
                        if (targets[targetIndex] == from)
                        {
                            targets[targetIndex] = to;
                        }
                    }
                }
            }

            for (var index = 0; index < body.ExceptionHandlers.Count; index++)
            {
                var handler = body.ExceptionHandlers[index];
                if (handler.TryStart == from)
                {
                    handler.TryStart = to;
                }

                if (handler.TryEnd == from)
                {
                    handler.TryEnd = to;
                }

                if (handler.HandlerStart == from)
                {
                    handler.HandlerStart = to;
                }

                if (handler.HandlerEnd == from)
                {
                    handler.HandlerEnd = to;
                }

                if (handler.FilterStart == from)
                {
                    handler.FilterStart = to;
                }
            }
        }

        private static void InsertBefore(ILProcessor il, Instruction anchor, List<Instruction> instructions)
        {
            Instruction previous = null;
            for (var index = 0; index < instructions.Count; index++)
            {
                var instruction = instructions[index];
                if (previous == null)
                {
                    il.InsertBefore(anchor, instruction);
                }
                else
                {
                    il.InsertAfter(previous, instruction);
                }

                previous = instruction;
            }
        }

        private static Dictionary<Instruction, Instruction> CloneInstructions(Instruction[] source)
        {
            var map = new Dictionary<Instruction, Instruction>(source.Length);
            for (var index = 0; index < source.Length; index++)
            {
                var instruction = source[index];
                var clone = Instruction.Create(OpCodes.Nop);
                clone.OpCode = instruction.OpCode;
                clone.Operand = instruction.Operand;
                map.Add(instruction, clone);
            }

            for (var index = 0; index < source.Length; index++)
            {
                var clone = map[source[index]];
                if (clone.Operand is Instruction target)
                {
                    clone.Operand = map[target];
                }
                else if (clone.Operand is Instruction[] targets)
                {
                    var remapped = new Instruction[targets.Length];
                    for (var targetIndex = 0; targetIndex < targets.Length; targetIndex++)
                    {
                        remapped[targetIndex] = map[targets[targetIndex]];
                    }

                    clone.Operand = remapped;
                }
            }

            return map;
        }

        private static void AddFastPathHandlers(
            MethodBody body,
            List<HandlerSnapshot> snapshots,
            Dictionary<Instruction, Instruction> map,
            Instruction slowStart)
        {
            for (var index = snapshots.Count - 1; index >= 0; index--)
            {
                var snapshot = snapshots[index];
                body.ExceptionHandlers.Insert(0, new ExceptionHandler(snapshot.HandlerType)
                {
                    CatchType = snapshot.CatchType,
                    TryStart = map[snapshot.TryStart],
                    TryEnd = snapshot.TryEnd == null ? slowStart : map[snapshot.TryEnd],
                    HandlerStart = map[snapshot.HandlerStart],
                    HandlerEnd = snapshot.HandlerEnd == null ? slowStart : map[snapshot.HandlerEnd],
                    FilterStart = snapshot.FilterStart == null ? null : map[snapshot.FilterStart],
                });
            }
        }

        private static List<HandlerSnapshot> SnapshotHandlers(MethodBody body)
        {
            var snapshots = new List<HandlerSnapshot>(body.ExceptionHandlers.Count);
            for (var index = 0; index < body.ExceptionHandlers.Count; index++)
            {
                var handler = body.ExceptionHandlers[index];
                snapshots.Add(new HandlerSnapshot(
                    handler.HandlerType,
                    handler.CatchType,
                    handler.TryStart,
                    handler.TryEnd,
                    handler.HandlerStart,
                    handler.HandlerEnd,
                    handler.FilterStart));
            }

            return snapshots;
        }

        private static AssemblyDefinition ResolveAssembly(AssemblyDefinition assembly, string name)
        {
            for (var index = 0; index < assembly.MainModule.AssemblyReferences.Count; index++)
            {
                var reference = assembly.MainModule.AssemblyReferences[index];
                if (reference.Name != name)
                {
                    continue;
                }

                try
                {
                    return assembly.MainModule.AssemblyResolver.Resolve(reference);
                }
                catch (AssemblyResolutionException)
                {
                    return null;
                }
            }

            return null;
        }

        private static TypeDefinition FindType(AssemblyDefinition assembly, string fullName)
        {
            for (var index = 0; index < assembly.MainModule.AssemblyReferences.Count; index++)
            {
                AssemblyDefinition resolved;
                try
                {
                    resolved = assembly.MainModule.AssemblyResolver.Resolve(assembly.MainModule.AssemblyReferences[index]);
                }
                catch (AssemblyResolutionException)
                {
                    continue;
                }

                var type = resolved?.MainModule.GetType(fullName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static FieldDefinition FindField(TypeDefinition type, string name)
        {
            for (var index = 0; index < type.Fields.Count; index++)
            {
                if (type.Fields[index].Name == name)
                {
                    return type.Fields[index];
                }
            }

            return null;
        }

        private static MethodDefinition FindMethod(TypeDefinition type, string name, params string[] parameterTypeNames)
        {
            for (var index = 0; index < type.Methods.Count; index++)
            {
                var method = type.Methods[index];
                if (method.Name != name || method.Parameters.Count != parameterTypeNames.Length)
                {
                    continue;
                }

                var match = true;
                for (var parameterIndex = 0; parameterIndex < parameterTypeNames.Length; parameterIndex++)
                {
                    if (method.Parameters[parameterIndex].ParameterType.FullName != parameterTypeNames[parameterIndex])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return method;
                }
            }

            return null;
        }

        private static MethodDefinition FindConstructor(TypeDefinition type)
        {
            for (var index = 0; index < type.Methods.Count; index++)
            {
                var method = type.Methods[index];
                if (method.IsConstructor && method.Parameters.Count == 0)
                {
                    return method;
                }
            }

            return null;
        }

        private static bool HasAttribute(ICustomAttributeProvider provider, string fullName)
        {
            return GetAttribute(provider, fullName) != null;
        }

        private static CustomAttribute GetAttribute(ICustomAttributeProvider provider, string fullName)
        {
            if (provider == null || !provider.HasCustomAttributes)
            {
                return null;
            }

            for (var index = 0; index < provider.CustomAttributes.Count; index++)
            {
                var attribute = provider.CustomAttributes[index];
                if (attribute.AttributeType.FullName == fullName)
                {
                    return attribute;
                }
            }

            return null;
        }

        private static string Display(MethodDefinition method)
        {
            return DisplayType(method.DeclaringType) + "." + method.Name;
        }

        private static string DisplayType(TypeReference type)
        {
            var name = type.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0)
            {
                name = name.Substring(0, tick);
            }

            return type.DeclaringType == null ? name : DisplayType(type.DeclaringType) + "." + name;
        }

        private static WeaveDiagnostic Create(MethodDefinition method, string message)
        {
            string file = null;
            var line = 0;
            var column = 0;
            try
            {
                if (method.DebugInformation != null && method.DebugInformation.HasSequencePoints)
                {
                    for (var index = 0; index < method.DebugInformation.SequencePoints.Count; index++)
                    {
                        var point = method.DebugInformation.SequencePoints[index];
                        if (point != null && !point.IsHidden)
                        {
                            file = point.Document?.Url;
                            line = point.StartLine;
                            column = point.StartColumn;
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
                file = null;
            }

            return new WeaveDiagnostic(message, file, line, column);
        }

        private readonly struct HandlerSnapshot
        {
            public HandlerSnapshot(
                ExceptionHandlerType handlerType,
                TypeReference catchType,
                Instruction tryStart,
                Instruction tryEnd,
                Instruction handlerStart,
                Instruction handlerEnd,
                Instruction filterStart)
            {
                HandlerType = handlerType;
                CatchType = catchType;
                TryStart = tryStart;
                TryEnd = tryEnd;
                HandlerStart = handlerStart;
                HandlerEnd = handlerEnd;
                FilterStart = filterStart;
            }

            public ExceptionHandlerType HandlerType { get; }

            public TypeReference CatchType { get; }

            public Instruction TryStart { get; }

            public Instruction TryEnd { get; }

            public Instruction HandlerStart { get; }

            public Instruction HandlerEnd { get; }

            public Instruction FilterStart { get; }
        }

        private readonly struct TagSite
        {
            public TagSite(string name, ParameterDefinition parameter, MethodReference setter)
            {
                Name = name;
                Parameter = parameter;
                Setter = setter;
            }

            public string Name { get; }

            public ParameterDefinition Parameter { get; }

            public MethodReference Setter { get; }
        }

        private readonly struct BoundTag
        {
            public BoundTag(TagSite site, FieldDefinition field, bool storeInStub)
            {
                Site = site;
                Field = field;
                StoreInStub = storeInStub;
            }

            public TagSite Site { get; }

            public FieldDefinition Field { get; }

            public bool StoreInStub { get; }
        }
    }
}
