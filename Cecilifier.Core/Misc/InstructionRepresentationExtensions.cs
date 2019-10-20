using Mono.Cecil.Cil;

namespace Cecilifier.Core.Misc
{
    public static class InstructionRepresentationExtensions
    {
        internal static InstructionRepresentation WithOperand(this OpCode opCode, string operand)
        {
            return new InstructionRepresentation {opCode = opCode, operand = operand};
        }
        
        internal static InstructionRepresentation WithInstructionMarker(this OpCode opCode, string marker)
        {
            return new InstructionRepresentation {opCode = opCode, tag = marker};
        }
        
        internal static InstructionRepresentation WithBranchOperand(this OpCode opCode, string branchTargetTag)
        {
            return new InstructionRepresentation {opCode = opCode, branchTargetTag = branchTargetTag};
        }
    }
}
