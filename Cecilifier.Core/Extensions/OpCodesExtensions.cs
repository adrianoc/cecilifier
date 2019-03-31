using Mono.Cecil.Cil;

namespace Cecilifier.Core.Extensions
{
    public static class OpCodesExtensions
    {
        public static string ConstantName(this OpCode opCode)
        {
            return "OpCodes." + opCode.Code;
        }

        public static bool IsCall(this OpCode opCode)
        {
            return opCode.Code == Code.Call || opCode.Code == Code.Calli || opCode.Code == Code.Callvirt;
        }
    }
}
