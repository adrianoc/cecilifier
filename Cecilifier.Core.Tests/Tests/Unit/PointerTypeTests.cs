using System.Collections.Generic;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    [TestFixture]
    public class PointerTypeTests : CecilifierUnitTestBase
    {
        [TestCaseSource(nameof(PointerAssignmentTypesToTest))]
        public void TestFixedIndirectAssignmentType(string type, string expectedOpCode)
        {
            var code = $"struct S {{ }} unsafe class C {{ {type} field; void M({type} value) {{ fixed({type} *p = &field) *p = value; }} }}";
            var result = RunCecilifier(code);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();

            Assert.That(cecilifiedCode, Does.Match(@"(il_M_\d+\.Emit\(OpCodes\.)Ldarg_0\);\s+" +
                                                   @"\1Ldflda, fld_field_\d+\);\s+" +
                                                   @"\1Stloc, l_p_\d+\);"));

            Assert.That(cecilifiedCode, Does.Match(@"(il_M_\d+\.Emit\(OpCodes\.)Ldloc, l_p_\d+\);\s+" +
                                                   @"\1Conv_U\);\s+" +
                                                   @"\1Ldarg_1\);\s+" +
                                                   $@"\1{expectedOpCode}\);"));
        }

        [TestCaseSource(nameof(PointerAssignmentTypesToTest))]
        public void TestAssignmentToLocalThroughPointerDeference(string type, string expectedStoreOpCode)
        {
            var code = $"struct S {{ }} unsafe class C {{ void M({type} *p, {type} value) {{ {type} *lp = p; *lp = value; }} }}";
            var result = RunCecilifier(code);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();

            Assert.That(cecilifiedCode, Does.Match(@"il_M_\d+\.Emit\(OpCodes\.Ldloc, l_lp_\d+\);\s+" +
                                                   @"\s+il_M_\d+\.Emit\(OpCodes\.Ldarg_2\);"));
            Assert.That(cecilifiedCode, Contains.Substring(expectedStoreOpCode));
        }

        [TestCaseSource(nameof(PointerAssignmentTypesToTest))]
        public void TestAssignmentToParameterThroughPointerDeference(string type, string expectedStoreOpCode)
        {
            var code = $"struct S {{ }} unsafe class C {{ void M({type} *p, {type} value) {{ *p = value; }} }}";
            var result = RunCecilifier(code);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            Assert.That(cecilifiedCode, Does.Match(@"il_M_\d+\.Emit\(OpCodes\.Ldarg_1\);\s+" +
                                                   @"\s+il_M_\d+\.Emit\(OpCodes\.Ldarg_2\);"));
            Assert.That(cecilifiedCode, Contains.Substring(expectedStoreOpCode));
        }

        private static IEnumerable<TestCaseData> PointerAssignmentTypesToTest()
        {
            yield return new TestCaseData("byte", "Stind_I1").SetName("byte");
            yield return new TestCaseData("short", "Stind_I2").SetName("short");
            yield return new TestCaseData("int", "Stind_I4").SetName("int");
            yield return new TestCaseData("uint", "Stind_I4").SetName("uint");
            yield return new TestCaseData("long", "Stind_I8").SetName("long");
            yield return new TestCaseData("ulong", "Stind_I8").SetName("ulong");
            yield return new TestCaseData("float", "Stind_R4").SetName("float");
            yield return new TestCaseData("double", "Stind_R8").SetName("double");
            yield return new TestCaseData("S", "Stobj, st_S_0").SetName("custom-struct");
        }
    }
}
