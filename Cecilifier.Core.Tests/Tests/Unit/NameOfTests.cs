using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    [TestFixture]
    public class NameOfTests : CecilifierUnitTestBase
    {
        [TestCase("using System; [Obsolete(nameof(Foo))] public class Foo { }", "new CustomAttributeArgument(assembly.MainModule.TypeSystem.String, \"Foo\")", TestName = "Attribute Parameter")]
        [TestCase("public class Foo { string ConstString = nameof(ConstString); }", "Ldstr, \"ConstString\"",TestName = "Field Initialization")]
        [TestCase("public class Foo { string Name() => nameof(Name); }", "Ldstr, \"Name\"",TestName = "Method Return")]
        [TestCase("public class Foo { string StringInterpolation() => $\"Name={nameof(StringInterpolation)}\"; }", "Ldstr, \"Name={0}\"",TestName = "String Interpolation")]
        public void Test(string code, string expectedLiteral)
        {
            var result = RunCecilifier(code);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            Assert.That(cecilifiedCode, Contains.Substring(expectedLiteral));
        }
    }
}
