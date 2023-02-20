using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class TypeTests : CecilifierUnitTestBase
{
    [TestCase("static class T {}", TestName = "Top Level")]
    [TestCase("class T { public static class Inner { } }", TestName = "Inner")]
    [TestCase("static class T { public static class Inner { } }", TestName = "Outer and Inner")]
    public void StaticTypes(string code)
    {
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Not.Contain("TypeAttributes.Static"));
        Assert.That(cecilifiedCode, Does.Contain("TypeAttributes.Abstract"));
        Assert.That(cecilifiedCode, Does.Contain("TypeAttributes.Sealed"));
    }
}
