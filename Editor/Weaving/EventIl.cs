using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Unimetry.Editor.Weaving
{
    internal static class EventIl
    {
        public static bool TryFindSequenceStart(Instruction call, out Instruction start)
        {
            start = call;
            if (!TryGetEffect(call, out _, out var callPop))
            {
                return false;
            }

            var pending = callPop;
            var current = call.Previous;
            var guard = 0;
            while (pending > 0 && current != null && guard++ < 48)
            {
                if (IsControlFlow(current.OpCode))
                {
                    return false;
                }

                if (!TryGetEffect(current, out var push, out var pop))
                {
                    return false;
                }

                start = current;
                pending -= push;
                if (pending < 0)
                {
                    return false;
                }

                pending += pop;
                current = current.Previous;
            }

            return pending == 0 && start != call;
        }

        public static bool TryGetLoadedVariable(MethodBody body, Instruction instruction, out VariableDefinition variable)
        {
            if (instruction.OpCode == OpCodes.Ldloc || instruction.OpCode == OpCodes.Ldloc_S)
            {
                variable = (VariableDefinition)instruction.Operand;
                return variable != null;
            }

            var index = -1;
            if (instruction.OpCode.Code == Code.Ldloc_0)
            {
                index = 0;
            }
            else if (instruction.OpCode.Code == Code.Ldloc_1)
            {
                index = 1;
            }
            else if (instruction.OpCode.Code == Code.Ldloc_2)
            {
                index = 2;
            }
            else if (instruction.OpCode.Code == Code.Ldloc_3)
            {
                index = 3;
            }

            if (index < 0 || index >= body.Variables.Count)
            {
                variable = null;
                return false;
            }

            variable = body.Variables[index];
            return true;
        }

        private static bool TryGetEffect(Instruction instruction, out int push, out int pop)
        {
            var code = instruction.OpCode.Code;
            if (code == Code.Call || code == Code.Callvirt || code == Code.Newobj)
            {
                var method = instruction.Operand as MethodReference;
                if (method == null)
                {
                    push = 0;
                    pop = 0;
                    return false;
                }

                if (code == Code.Newobj)
                {
                    pop = method.Parameters.Count;
                    push = 1;
                    return true;
                }

                pop = method.Parameters.Count + (method.HasThis ? 1 : 0);
                push = method.ReturnType.MetadataType == MetadataType.Void ? 0 : 1;
                return true;
            }

            if (!TryCount(instruction.OpCode.StackBehaviourPush, out push) ||
                !TryCount(instruction.OpCode.StackBehaviourPop, out pop))
            {
                push = 0;
                pop = 0;
                return false;
            }

            return true;
        }

        private static bool TryCount(StackBehaviour behaviour, out int count)
        {
            switch (behaviour)
            {
                case StackBehaviour.Pop0:
                case StackBehaviour.Push0:
                    count = 0;
                    return true;
                case StackBehaviour.Pop1:
                case StackBehaviour.Popi:
                case StackBehaviour.Popref:
                case StackBehaviour.Push1:
                case StackBehaviour.Pushi:
                case StackBehaviour.Pushi8:
                case StackBehaviour.Pushr4:
                case StackBehaviour.Pushr8:
                case StackBehaviour.Pushref:
                    count = 1;
                    return true;
                case StackBehaviour.Pop1_pop1:
                case StackBehaviour.Popi_pop1:
                case StackBehaviour.Popi_popi:
                case StackBehaviour.Popi_popi8:
                case StackBehaviour.Popi_popr4:
                case StackBehaviour.Popi_popr8:
                case StackBehaviour.Popref_pop1:
                case StackBehaviour.Popref_popi:
                case StackBehaviour.Push1_push1:
                    count = 2;
                    return true;
                case StackBehaviour.Popi_popi_popi:
                case StackBehaviour.Popref_popi_popi:
                case StackBehaviour.Popref_popi_popi8:
                case StackBehaviour.Popref_popi_popr4:
                case StackBehaviour.Popref_popi_popr8:
                case StackBehaviour.Popref_popi_popref:
                    count = 3;
                    return true;
                default:
                    count = 0;
                    return false;
            }
        }

        private static bool IsControlFlow(OpCode opcode)
        {
            return opcode.FlowControl == FlowControl.Branch ||
                   opcode.FlowControl == FlowControl.Cond_Branch ||
                   opcode.FlowControl == FlowControl.Return ||
                   opcode.FlowControl == FlowControl.Throw ||
                   opcode.FlowControl == FlowControl.Break;
        }
    }
}
