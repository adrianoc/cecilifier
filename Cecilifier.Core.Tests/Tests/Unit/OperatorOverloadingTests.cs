using Microsoft.CodeAnalysis;
using Mono.Cecil.Cil;
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
        public void TestUserConversionOperators(string source, bool isImplicit)
        {
            var expectedMethodOperator = $"op_{(isImplicit ? "Implicit" : "Explicit")}";
            var result = RunCecilifier(source);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            
            var expectedOperatorMethod = $"new MethodDefinition\\(\"{expectedMethodOperator}\", MethodAttributes.Public \\| MethodAttributes.Static\\| MethodAttributes.SpecialName \\| MethodAttributes.HideBySig,.*\\);";
            Assert.That(cecilifiedCode, Does.Match(expectedOperatorMethod), $"Operator method not defined. Cecilified code:{NewLine}{NewLine}{cecilifiedCode}");
            
            Assert.That(cecilifiedCode, Contains.Substring($"OpCodes.Call, m_{expectedMethodOperator}_1"), $"call to operator method not found. Cecilified code:{NewLine}{NewLine}{cecilifiedCode}");
        }

        [TestCase("public static int operator+(Foo lhs, Foo rhs) => 0;", "int Call() => this + this;", WellKnownMemberNames.AdditionOperatorName)]
        [TestCase("public static int operator-(Foo lhs, Foo rhs) => 0;", "int Call() => this - this;", WellKnownMemberNames.SubtractionOperatorName)]
        [TestCase("public static int operator*(Foo lhs, Foo rhs) => 0;", "int Call() => this * this;", WellKnownMemberNames.MultiplyOperatorName)]
        [TestCase("public static int operator/(Foo lhs, Foo rhs) => 0;", "int Call() => this / this;", WellKnownMemberNames.DivisionOperatorName)]
        [TestCase("public static int operator%(Foo lhs, Foo rhs) => 0;", "int Call() => this % this;", WellKnownMemberNames.ModulusOperatorName)]
        [TestCase("public static int operator&(Foo lhs, Foo rhs) => 0;", "int Call() => this & this;", WellKnownMemberNames.BitwiseAndOperatorName)]
        [TestCase("public static int operator|(Foo lhs, Foo rhs) => 0;", "int Call() => this | this;", WellKnownMemberNames.BitwiseOrOperatorName)]
        [TestCase("public static int operator<<(Foo lhs, int rhs) => 0;", "int Call() => this << 42;", WellKnownMemberNames.LeftShiftOperatorName)]
        [TestCase("public static int operator>>(Foo lhs, int rhs) => 0;", "int Call() => this >> 42;", WellKnownMemberNames.RightShiftOperatorName)]
        [TestCase("public static bool operator!=(Foo lhs, Foo rhs) => false; public static bool operator==(Foo lhs, Foo rhs) => false;", "bool Call() => this != this;", WellKnownMemberNames.InequalityOperatorName)]
        [TestCase("public static bool operator==(Foo lhs, Foo rhs) => false; public static bool operator!=(Foo lhs, Foo rhs) => false;", "bool Call() => this == this;", WellKnownMemberNames.EqualityOperatorName)]
        [TestCase("public static bool operator<(Foo lhs, Foo rhs) => false; public static bool operator>(Foo lhs, Foo rhs) => false;", "bool Call() => this < this;", WellKnownMemberNames.LessThanOperatorName)]
        [TestCase("public static bool operator>(Foo lhs, Foo rhs) => false; public static bool operator<(Foo lhs, Foo rhs) => false;", "bool Call() => this > this;", WellKnownMemberNames.GreaterThanOperatorName)]
        [TestCase("public static bool operator<=(Foo lhs, Foo rhs) => false; public static bool operator >=(Foo lhs, Foo rhs) => false;", "bool Call() => this <= this;", WellKnownMemberNames.LessThanOrEqualOperatorName)]
        [TestCase("public static bool operator>=(Foo lhs, Foo rhs) => false; public static bool operator<=(Foo lhs, Foo rhs) => false;", "bool Call() => this >= this;", WellKnownMemberNames.GreaterThanOrEqualOperatorName)]
        [TestCase("public static int operator+(Foo lhs) => 0;", "int Call() => +this;", WellKnownMemberNames.UnaryPlusOperatorName)]
        [TestCase("public static int operator-(Foo lhs) => 0;", "int Call() => -this;", WellKnownMemberNames.UnaryNegationOperatorName)]
        [TestCase("public static int operator!(Foo lhs) => 0;", "int Call() => !this;", WellKnownMemberNames.LogicalNotOperatorName)]
        [TestCase("public static int operator~(Foo lhs) => 0;", "int Call() => ~this;", WellKnownMemberNames.OnesComplementOperatorName)]
        [TestCase("public static Foo operator++(Foo lhs) => lhs;", "Foo Call(Foo o) => o++;", WellKnownMemberNames.IncrementOperatorName)]
        [TestCase("public static Foo operator--(Foo lhs) => lhs;", "Foo Call(Foo o) => o--;", WellKnownMemberNames.DecrementOperatorName)]
        public void TestOperators(string code, string callOperator, string expectedMethodOperator)
        {
            var toBeCecilified = $"class Foo {{ {code} {callOperator} }}";
            var result = RunCecilifier(toBeCecilified);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            
            var expectedOperatorMethod = $"new MethodDefinition\\(\"{expectedMethodOperator}\", MethodAttributes.Public \\| MethodAttributes.Static\\| MethodAttributes.SpecialName \\| MethodAttributes.HideBySig, .*\\);";
            Assert.That(cecilifiedCode, Does.Match(expectedOperatorMethod), $"Operator method not defined. Cecilified code:{NewLine}{NewLine}{cecilifiedCode}");
            
            Assert.That(cecilifiedCode, Contains.Substring($"Emit(OpCodes.Call, m_{expectedMethodOperator}_1"), $"call to operator method not found. Cecilified code:{NewLine}{NewLine}{cecilifiedCode}");

            foreach (var notExpectedInstruction in notExpectedInstructions)
            {
                Assert.That(cecilifiedCode, Does.Not.Contains($".Emit(OpCodes.{notExpectedInstruction})"), $"Unexpected `{notExpectedInstruction}` instruction found. Cecilified code:{NewLine}{NewLine}{cecilifiedCode}");    
            }
        }

        [TestCase("int Op(int a, int b) => a + b;", "Add")]
        [TestCase("int Op(int a, int b) => a - b;", "Sub")]
        [TestCase("int Op(int a, int b) => a * b;", "Mul")]
        [TestCase("int Op(int a) => ~a;", "Not")]
        [TestCase("int Op(int a, int b) => a << b;", "Shl")]
        [TestCase("int Op(int a, int b) => a >> b;", "Shr")]
        public void Test_OperatorsOnPrimitives_ArePreserved(string snippetWithOperator, string expectedInstruction)
        {
            var toBeCecilified = $"class Foo {{ {snippetWithOperator} }}";
            var result = RunCecilifier(toBeCecilified);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            
            Assert.That(cecilifiedCode, Contains.Substring($"Emit(OpCodes.{expectedInstruction}"));
        }

        [Test]
        public void Test_OperatorOverloading_InStructs()
        {
            var toBeCecilified = "struct Bar { public static int operator+(Bar lhs, Bar rhs) => 0; int Add(Bar lhs, Bar rhs) => lhs + rhs; }";
            var result = RunCecilifier(toBeCecilified);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            
            var expectedOperatorMethod = $"new MethodDefinition(\"op_Addition\", MethodAttributes.Public | MethodAttributes.Static| MethodAttributes.SpecialName | MethodAttributes.HideBySig, assembly.MainModule.TypeSystem.Int32);";
            Assert.That(cecilifiedCode, Contains.Substring(expectedOperatorMethod), $"Operator method not defined. Cecilified code:{NewLine}{NewLine}{cecilifiedCode}");
            
            Assert.That(cecilifiedCode, Contains.Substring($"Emit(OpCodes.Call, m_op_Addition_1"), $"call to operator method not found. Cecilified code:{NewLine}{NewLine}{cecilifiedCode}");

            foreach (var notExpectedInstruction in notExpectedInstructions)
            {
                Assert.That(cecilifiedCode, Does.Not.Contains($".Emit(OpCodes.{notExpectedInstruction})"), $"Unexpected `{notExpectedInstruction}` instruction found. Cecilified code:{NewLine}{NewLine}{cecilifiedCode}");    
            }
        }
        
        private static readonly string[] notExpectedInstructions = new[]
        {
            "Add",
            "Sub",
            "Mul",
            "Div",
            "Rem",
            "Not",
            "Shl",
            "Shr",
            "And",
        };

    }
}
