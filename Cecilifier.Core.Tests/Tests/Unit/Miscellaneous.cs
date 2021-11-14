using System;
using System.ComponentModel.DataAnnotations;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    [TestFixture]
    public class MiscellaneousTests : CecilifierUnitTestBase
    {
        [Test]
        public void Test_Issue110()
        {
            var code = "class Foo { void Bar(string s) { var index = 2; Bar(s.Substring(0, index)); } }";
            var expectedSnippet = @".*(?<emit>.+\.Emit\(OpCodes\.)Ldarg_0\);\s" 
                                  + @"\1Ldarg_1.+;\s"
                                  + @"\1Ldc_I4, 0.+;\s"
                                  + @"\1Ldloc, l_index_4.+;\s"
                                  + @"\1Callvirt, .+""Substring"".+;\s"
                                  + @"\1Call, m_bar_1.+;\s"
                                  + @"\1Ret.+;";
            
            var result = RunCecilifier(code);
            Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expectedSnippet));
        }

        [Test]
        public void CustomDelegate_ComparisonToNull_GeneratesCeqInstruction_InsteadOfCalling_Operator()
        {
            var code = @"using System;
public delegate object CustomDelegate(int arg);
public static class ObjectMaker
{
	public static bool Test(CustomDelegate cd) => cd == null;
}";
            var expectedCecilifiedCode = @"il_test_10.Emit\(OpCodes.Ldarg_0\).+\s+" +
                                         @"il_test_10.Emit\(OpCodes.Ldnull\).+\s+" +
                                         @"il_test_10.Emit\(OpCodes.Ceq\).+\s+" +
                                         @"il_test_10.Emit\(OpCodes.Ret\);";
            
            var result = RunCecilifier(code);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            Assert.That(cecilifiedCode, Does.Match(expectedCecilifiedCode), cecilifiedCode);
        }
        
    }
}
