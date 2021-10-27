using System;
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
    }
}
