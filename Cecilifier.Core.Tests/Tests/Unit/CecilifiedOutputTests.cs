using System.IO;
using System.Text;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class CecilifiedOutputTests : CecilifierUnitTestBase
{
    [TestCase(
        @"class Foo { string s = @""1
                                    2
                                    3"";}",
        "string s = @\"1...",
        TestName = "String Field")]
    [TestCase(
        @"class Foo { int i = 13 +
                              29; }",
        "int i = 13 +...",
        TestName = "Int Field")]
    [TestCase(
        @"class Foo { bool b = true &&
                               false;}",
        "bool b = true &&...",
        TestName = "Bool Field")]
    public void TestMultilineMemberInitialization_IsProperlyCommented(string code, string expected)
    {
        var result = RunCecilifier(code);
        Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring(expected));
    }

    [TestCase(@"class Foo
{
void M(int value)
{
    value = value + 
            value;
}
}", 
        "value = value + ...", 
        TestName = "Assignment")]
    [TestCase(@"void Test<T>(int value)
		where T : struct { }", 
        "void Test<T>(int value)...", 
        TestName = "Generic Method Constraint")]
    public void MultilineExpressions(string code, string expected)
    {
        var result = RunCecilifier(code);
        Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring(expected));
    }
    
    [Test]
    public void CompoundStatement_WithBraceInSameLine_GeneratesValidComments()
    {
        var code = @"
using static System.Console;
public class Foo
{
	void Bar(int i) { WriteLine(i); }

	void BarBaz(int i) 
    {
        if (i > 42) {
            WriteLine(i);
        }
    }
}";
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Contains.Substring("//Parameters of 'void Bar(int i) { WriteLine(i); }'"));
        Assert.That(cecilifiedCode, Contains.Substring("//if (i > 42) {..."));
    }

    [Test]
    public void LocalVariableDeclarations_Are_CommentedOut()
    {
        AssertCecilifiedCodeContainsSnippet(
            "class C { int S(int i, int j) { int l = i / 2; return l + j; } }",
            "//int l = i / 2;");
    }

    private void AssertCecilifiedCodeContainsSnippet(string code, string expectedSnippet)
    {
        var cecilifier = Cecilifier.Process(new MemoryStream(Encoding.UTF8.GetBytes(code)), new CecilifierOptions {References = Utils.GetTrustedAssembliesPath() });
        var generated = cecilifier.GeneratedCode.ReadToEnd();

        Assert.That(generated, Does.Contain(expectedSnippet), "Expected snippet not found");
    }
}
