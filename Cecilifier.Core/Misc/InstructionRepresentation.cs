using Mono.Cecil.Cil;

namespace Cecilifier.Core.Misc
{
    struct InstructionRepresentation
    {
        public OpCode opCode;
        public string operand;
        public string branchTargetTag;
        public string tag;

        public static implicit operator InstructionRepresentation(OpCode opCode)
        {
            return new InstructionRepresentation { opCode = opCode, operand = null, tag = null };
        }
    }
}
