using System.Text.RegularExpressions;
using Mono.Cecil.Cil;
using NuGet.Frameworks;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    [TestFixture]
    public class ConstTests : CecilifierUnitTestBase
    {
        [Test]
        public void ConstFieldDeclaration_DoesNotIntroduce_FieldStore()
        {
            var result = RunCecilifier("class C { public const int IntValue = 42; }");
            using var reader = result.GeneratedCode;

            var cecilifiedCode = reader.ReadToEnd();
            Assert.That(cecilifiedCode, Does.Match("new FieldDefinition\\(\"IntValue\", .*FieldAttributes\\.Literal \\| FieldAttributes\\.Static.*\\) { Constant = 42 }"), "Expected constant initialization not found.");

            var regex = new Regex("var (?<fieldVar>.+) = new FieldDefinition\\(.*\\).+\\s+.+\\.Emit\\(OpCodes\\.Stfld, \\1\\)", RegexOptions.Singleline);
            Assert.That(cecilifiedCode, Does.Not.Match(regex), "Store field not expected");
        }

        [Test]
        public void ConstUsage()
        {
            var result = RunCecilifier("class C { public const int IntValue = 42; int Foo() => IntValue; }");
            using var reader = result.GeneratedCode;

            var cecilifiedCode = reader.ReadToEnd();
            var regex = new Regex("var (?<fieldVar>.+) = new FieldDefinition\\(.*\\).+\\s+.+\\.Emit\\(OpCodes\\.Ldsfld, \\1\\)", RegexOptions.Singleline);
            Assert.That(cecilifiedCode, Does.Not.Match(regex), "Unexpected `ldfld` instruction. Value should be treated as a constant and be inlined.");
            Assert.That(cecilifiedCode, Contains.Substring(".Emit(OpCodes.Ldc_I4, 42)"));
        }

        [TestCase("class C { public const int IntValue = -42; int Foo() => IntValue; }", Code.Ldc_I4)]
        [TestCase("class C { public const uint UIntValue = 42; uint Foo() => UIntValue; }", Code.Ldc_I4)]
        [TestCase("class C { public const bool BoolValue = true; bool Foo() => BoolValue; }", Code.Ldc_I4)]
        [TestCase("class C { public const double DoubleValue = 42.42; double Foo() => DoubleValue; }", Code.Ldc_R8)]
        [TestCase("class C { public const float FloatValue = 42.42f; float Foo() => FloatValue; }", Code.Ldc_R4)]
        [TestCase("class C { public const string StringValue = \"foo\"; int Foo() => StringValue.Length; }", Code.Ldstr)]
        [TestCase("class C { public const byte ByteValue = 42; byte Foo() => ByteValue; }", Code.Ldc_I4)]
        [TestCase("class C { public const sbyte SByteValue = -42; sbyte Foo() => SByteValue; }", Code.Ldc_I4)]
        [TestCase("class C { public const sbyte SByteValue = -42; sbyte Foo() => SByteValue; }", Code.Ldc_I4)]
        [TestCase("class C { public const C NullRef = null; C Foo() => NullRef; }", Code.Ldnull)]
        public void ConstTypes(string code, Code expectedLoad)
        {
            var result = RunCecilifier(code);
            using var reader = result.GeneratedCode;
            var cecilifiedCode = reader.ReadToEnd();

            Assert.That(cecilifiedCode, Contains.Substring(expectedLoad.ToString()));
        }

        [TestCase("using System; class C { public const ConsoleColor EnumConst = ConsoleColor.Blue; }", Code.Ldc_I4, "9")]
        [TestCase("class C { public const int IntValue = 42; public const int IntValue2 = IntValue + 42; }", Code.Ldc_I4, "84")]
        [TestCase("class C { public const string StringValue = \"foo\"; }", Code.Ldstr, "\"foo\"")]
        public void ConstValueValue(string code, Code expectedLoad, string expectedLoadedValue)
        {
            var result = RunCecilifier(code);
            using var reader = result.GeneratedCode;
            var cecilifiedCode = reader.ReadToEnd();

            Assert.That(cecilifiedCode, Contains.Substring($"Constant = {expectedLoadedValue}"));
        }
    }
}
