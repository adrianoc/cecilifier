using static System.Environment;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    [TestFixture]
    public class OperatorOverloadingTests : CecilifierUnitTestBase
    {
        [TestCase("public class Foo { public static implicit operator int(Foo f) => 42; int Get(Foo f) => f; }", true, TestName = "Implicit Numeric")]
        [TestCase("public class Foo { public static explicit operator int(Foo f) => 42; int Get(Foo f) => (int) f;}", false, TestName = "Explicit Numeric")]
        [TestCase("public class Foo { public static explicit operator Foo(int i) => null; Foo Get(int i) => (Foo) i;}", false, TestName = "Explicit Custom Class")]
        public void Test(string source, bool isImplicit)
        {
            var expectedMethodOperator = $"op_{(isImplicit ? "Implicit" : "Explicit")}";
            var result = RunCecilifier(source);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            
            var expectedOperatorMethod = $"new MethodDefinition(\"{expectedMethodOperator}\", MethodAttributes.Public | MethodAttributes.Static| MethodAttributes.SpecialName | MethodAttributes.HideBySig, assembly.MainModule.TypeSystem.Void);";
            Assert.That(cecilifiedCode, Contains.Substring(expectedOperatorMethod), $"Operator method not defined. Cecilified code:{NewLine}{NewLine}{cecilifiedCode}");
            
            Assert.That(cecilifiedCode, Contains.Substring($"OpCodes.Call, m_{expectedMethodOperator}_1"), $"call to operator method not found. Cecilified code:{NewLine}{NewLine}{cecilifiedCode}");
        }
    }
}
