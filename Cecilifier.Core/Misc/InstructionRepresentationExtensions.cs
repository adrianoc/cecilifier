using System.Runtime.CompilerServices;
using System.Reflection.Emit;

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

        internal static InstructionRepresentation IgnoreIf(this InstructionRepresentation self, bool shouldIgnore, [CallerArgumentExpression(nameof(shouldIgnore))] string expression = null)
        {
            return self with { Ignore = shouldIgnore };
        }
    }
}
