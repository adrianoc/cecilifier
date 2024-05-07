using Mono.Cecil.Cil;

namespace Cecilifier.Core.Misc
{
    record struct InstructionRepresentation(OpCode OpCode, string Operand, string BranchTargetTag, string Tag)
    {
        public static implicit operator InstructionRepresentation(OpCode opCode)
        {
            return new InstructionRepresentation { OpCode = opCode, Operand = null, Tag = null };
        }
    }
}
