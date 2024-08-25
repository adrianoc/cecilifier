using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class ParameterTests : CecilifierUnitTestBase
{
    [TestCase("Int32", "-42", TestName = "Integer Negative")]
    [TestCase("Int32", "10", TestName = "Integer Positive (implicit)")]
    [TestCase("Int32", "+10", "10", TestName = "Integer Positive (explicit)")]
    [TestCase("Int32", "0x42", "66", TestName = "Integer Hex")]
    [TestCase("Boolean", "true", TestName = "Boolean True")]
    [TestCase("Boolean", "false", TestName = "Boolean False")]
    [TestCase("Single", "4.2f", TestName = "Float")]
    [TestCase("Double", "4.2", "4.2d", TestName = "Double Implicit")]
    [TestCase("Double", "4.2d", "4.2d", TestName = "Double Explicit")] // float point literals without suffixes are double by default.
    [TestCase("String", "\"Foo\"", TestName = "String")]
    [TestCase("Object", "null", TestName = "Object Null")]
    [TestCase("Object", "default", "null", TestName = "Object Default")]
    public void TestDefaultParameterInDefinition(string paramType, string paramValue, string expectedParamValue = null)
    {
        var result = RunCecilifier($"using System; class Foo {{ void Bar(string s, {paramType} p = {paramValue}) {{ }} }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilifiedCode, Contains.Substring("ParameterDefinition(\"s\", ParameterAttributes.None, assembly.MainModule.TypeSystem.String)"));
        Assert.That(cecilifiedCode, Does.Match($@"var (?<param_def>.*) = new ParameterDefinition\(""p"", ParameterAttributes.Optional, assembly.MainModule.TypeSystem.{paramType}\);\s+" +
                                               $@"\k<param_def>.Constant = {Regex.Escape(expectedParamValue ?? paramValue)};"));
    }

    [TestCase("Int32", "Ldc_I4", "42")]
    [TestCase("String", "Ldstr", "\"Foo\"")]
    [TestCase("Boolean", "Ldc_I4", "true", "1")]
    [TestCase("Boolean", "Ldc_I4", "false", "0")]
    [TestCase("Double", "Ldc_R8", "4.2")]
    [TestCase("Single", "Ldc_R4", "4.2f")]
    [TestCase("Object", "Ldnull", "null", "")]
    public void TestDefaultParameterInInvocations(string paramType, string ilOpCode, string paramValue, string expectedParamValue = null)
    {
        var result = RunCecilifier($"using System; class Foo {{ void WithDefault(bool b, {paramType} p = {paramValue}) {{ }} void Execute() => WithDefault(true); }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        expectedParamValue = expectedParamValue == null || expectedParamValue.Length > 0 ? $", {expectedParamValue ?? paramValue}" : "";
        Assert.That(cecilifiedCode, Does.Match($@"Ldc_I4, 1.+\s+.+{ilOpCode}{expectedParamValue}.+\s+.+Call, m_withDefault_1"));
    }

    [Test]
    public void TestInParameter()
    {
        var result = RunCecilifier("struct Foo { void Bar(in Foo f) { } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Contains.Substring("ParameterDefinition(\"f\", ParameterAttributes.In, st_foo_0.MakeByReferenceType()"));
    }

    [Test]
    public void TestOutParameter()
    {
        var result = RunCecilifier("class Foo { void Bar(out string s) { s = null; } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Contains.Substring("ParameterDefinition(\"s\", ParameterAttributes.Out, assembly.MainModule.TypeSystem.String.MakeByReferenceType())"));
    }
}
