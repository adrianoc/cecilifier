using System.Collections.Generic;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.Extensions
{
	public static class OpCodesExtensions
	{
		public static string ConstantName(this OpCode opCode)
		{
			return opCodesConstantNames[opCode];
		}

		static OpCodesExtensions()
		{
			foreach (var field in typeof(OpCodes).GetFields())
			{
				if (field.FieldType != typeof(OpCode)) continue;

				opCodesConstantNames[(OpCode) field.GetValue(null)] = "OpCodes." + field.Name;
			}
		}
		private static IDictionary<OpCode, string> opCodesConstantNames = new Dictionary<OpCode, string>();
	}
}
