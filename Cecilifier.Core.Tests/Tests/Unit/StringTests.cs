using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class StringTests : CecilifierUnitTestBase
{
    [TestCase(""""const string rls = """This "is a" test"""; System.Console.WriteLine(rls);"""", TestName = "Constant")]
    [TestCase(""""System.Console.WriteLine("""This "is a" test""");"""", TestName = "UsedAsParameter")]
    [TestCase(""""Foo(); void Foo(string s = """This "is a" test""") {}"""", TestName = "AsDefaultParameterValue")]
    public void TestRawLiteralString(string code)
    {
        var result = RunCecilifier(code);

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Contains.Substring("""Ldstr, "This \"is a\" test"""));
    }

    [TestCase(RawStringConstants.NoIndentation, RawStringConstants.ResultingNoIndentation)]
    [TestCase(RawStringConstants.SimpleIndentation, RawStringConstants.ResultingSimpleIndentation)]
    public void TestMultiLineRawLiteralString(string value, string expected)
    {
        Run($$""""var s = """{{value}}"""; System.Console.WriteLine(s);"""", expected);
    }

    [TestCase(RawStringConstants.NoIndentation, RawStringConstants.ResultingNoIndentation)]
    [TestCase(RawStringConstants.SimpleIndentation, RawStringConstants.ResultingSimpleIndentation)]
    public void TestMultiLineRawLiteralStringAsDefaultParameterValue(string value, string expected)
    {
        Run($$""""Foo(); void Foo(string s = """{{value}}""") {}"""", expected);
    }

    private static void Run(string code, string expectedString)
    {
        var result = RunCecilifier(code);

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Contains.Substring($"Ldstr, \"{expectedString}\""));
    }
}

struct RawStringConstants
{
    public const string NoIndentation = "\nA\n              \nB\n";
    public const string ResultingNoIndentation = "A\\n              \\nB";

    public const string SimpleIndentation = "\n  A\n  B\n  ";
    public const string ResultingSimpleIndentation = "A\\nB";
}
