using Mono.Cecil.Cil;

namespace Cecilifier.Core.Extensions
{
	public static class OpCodesExtensions
	{
		public static string ConstantName(this OpCode opCode)
		{
			return "OpCodes." + opCode.Code;
		}
	}
}
