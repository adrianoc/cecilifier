using System.Collections.Generic;
using System.Linq;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.Extensions
{
    public static class OpCodesExtensions
    {
        // In the IL specification there is no instruction with code == 26 (0x24) (See Ecma-335 - Partition I II.1.2.1 OpCode encodings, https://ecma-international.org/publications-and-standards/standards/ecma-335/)
        // but Mono.Cecil.Cil.Code enum ignores that, meaning that in Mono.Cecil opcodes after 0x23 (Ldc_R8) are offset by 1
        // i.e. `Dup` has a value of 0x25 in the specification but `0x24` in Mono.Cecil. 
        // This table contains the gaps (offsets) that need to be applied for values greater than the keys. For instance, for IL code 0xA7 we would need to subtract 16 since 0xA7 > 0xA5 
        static readonly Dictionary<ushort, int> _offsetGaps = new()
        {
            [0] = 0,
            [0x23] = 1,
            [0x76] = 3,
            [0xA5] = 16,
            [0xBA] = 23,
            [0xC3] = 25,
            [0xC6] = 34,
            [0xE0] = 64833,
            [0xFE07] = 64834,
            [0xFE0F] = 64835,
            //[0xFE18] = 64836, // 0xFE19 (no) is actually mapped in Mono.Cecil.Cil.Code
            [0xFE1A] = 64836,
        };
        
        public static string OpCodeName(this System.Reflection.Emit.OpCode opCode)
        {
            ushort opCodeValue = (ushort) opCode.Value;
            var opCodeOffset = _offsetGaps.Keys.OrderByDescending(value => value).FirstOrDefault(value => opCodeValue > value);
            return ((Code) (opCodeValue - _offsetGaps[opCodeOffset])).ToString(); // See comment in the declaration of `_offsetGaps`
        }
        
        public static string ConstantName(this System.Reflection.Emit.OpCode opCode)
        {
            return $"OpCodes.{opCode.OpCodeName()}";
        }

        public static bool IsCallOrNewObj(this OpCode opCode)
        {
            return opCode.Code == Code.Call || opCode.Code == Code.Calli || opCode.Code == Code.Callvirt || opCode.Code == Code.Newobj;
        }
    }
}
