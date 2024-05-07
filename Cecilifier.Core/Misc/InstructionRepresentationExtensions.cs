using Mono.Cecil.Cil;

namespace Cecilifier.Core.Misc
{
    public static class InstructionRepresentationExtensions
    {
        internal static InstructionRepresentation WithOperand(this OpCode opCode, string operand)
        {
            return new InstructionRepresentation { OpCode = opCode, Operand = operand };
        }

        internal static InstructionRepresentation WithInstructionMarker(this OpCode opCode, string marker)
        {
            return new InstructionRepresentation { OpCode = opCode, Tag = marker };
        }

        internal static InstructionRepresentation WithBranchOperand(this OpCode opCode, string branchTargetTag)
        {
            return new InstructionRepresentation { OpCode = opCode, BranchTargetTag = branchTargetTag };
        }
    }
}
