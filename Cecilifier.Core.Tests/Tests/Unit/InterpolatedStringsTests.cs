using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    [TestFixture]
    public class InterpolatedStringsTests : CecilifierUnitTestBase
    {
        [Test]
        public void With2Values()
        {
            var code = "class C { void M() { int i = 3; object o = null; var s = $\"i={i}, o={o}\"; System.Console.WriteLine(s); } }";
            var result = RunCecilifier(code);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            Assert.That(
                cecilifiedCode,
                Does.Match("(.+\\.Emit\\(OpCodes\\.)Ldstr, \"i=\\{0\\}, o=\\{1\\}\"\\);\\s+\\1Ldloc, l_i_3\\);\\s+\\1Box, assembly.MainModule.TypeSystem.Int32\\);\\s+\\1Ldloc, l_o_4\\);"));
        }

        [Test]
        public void WithMoreThan4Values()
        {
            var code = "class C { void M(int i, object o) { var s = $\"1={i+1}, 2={o}, 3={3}, 4={4}, 5={5}\"; System.Console.WriteLine(s); } }";
            var result = RunCecilifier(code);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            var expected = @"//var s = $""1={i+1}, 2={o}, 3={3}, 4={4}, 5={5}"";
			var l_s_5 = new VariableDefinition(assembly.MainModule.TypeSystem.String);
			m_M_1.Body.Variables.Add(l_s_5);
			il_M_2.Emit(OpCodes.Ldstr, ""1={0}, 2={1}, 3={2}, 4={3}, 5={4}"");
			il_M_2.Emit(OpCodes.Ldc_I4, 5);
			il_M_2.Emit(OpCodes.Newarr, assembly.MainModule.TypeSystem.Object);
			il_M_2.Emit(OpCodes.Dup);
			il_M_2.Emit(OpCodes.Ldc_I4, 0);
			il_M_2.Emit(OpCodes.Ldarg_1);
			il_M_2.Emit(OpCodes.Ldc_I4, 1);
			il_M_2.Emit(OpCodes.Add);
			il_M_2.Emit(OpCodes.Box, assembly.MainModule.TypeSystem.Int32);
			il_M_2.Emit(OpCodes.Stelem_Ref);
			il_M_2.Emit(OpCodes.Dup);
			il_M_2.Emit(OpCodes.Ldc_I4, 1);
			il_M_2.Emit(OpCodes.Ldarg_2);
			il_M_2.Emit(OpCodes.Stelem_Ref);
			il_M_2.Emit(OpCodes.Dup);
			il_M_2.Emit(OpCodes.Ldc_I4, 2);
			il_M_2.Emit(OpCodes.Ldc_I4, 3);
			il_M_2.Emit(OpCodes.Box, assembly.MainModule.TypeSystem.Int32);
			il_M_2.Emit(OpCodes.Stelem_Ref);
			il_M_2.Emit(OpCodes.Dup);
			il_M_2.Emit(OpCodes.Ldc_I4, 3);
			il_M_2.Emit(OpCodes.Ldc_I4, 4);
			il_M_2.Emit(OpCodes.Box, assembly.MainModule.TypeSystem.Int32);
			il_M_2.Emit(OpCodes.Stelem_Ref);
			il_M_2.Emit(OpCodes.Dup);
			il_M_2.Emit(OpCodes.Ldc_I4, 4);
			il_M_2.Emit(OpCodes.Ldc_I4, 5);
			il_M_2.Emit(OpCodes.Box, assembly.MainModule.TypeSystem.Int32);
			il_M_2.Emit(OpCodes.Stelem_Ref);";

            Assert.That(cecilifiedCode, Contains.Substring(expected), "With more than 3 parameters, String.Format() taking an array of objects should have been called.");
        }
    }
}
