using System.Text.RegularExpressions;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
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

    [TestCase("class C { int this[int i] => i; int this[string s] => s.Length; }", TestName = "Multiple indexers")]
    [TestCase("class C { int this[int i] => i; }", TestName = "One indexer")]
    [TestCase("class C { int this[int i] => i; } class Other { int this[int i] => i; } ", TestName = "Indexers in multiple types")]
    public void TypeWithIndexers_OnlyOneDefaultMemberAttributeIsAdded(string codeToTest)
    {
        var result = RunCecilifier(codeToTest);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var matches = Regex.Matches(cecilifiedCode, @"cls_C_\d+.CustomAttributes.Add\(attr_defaultMember_\d+\);");

        Assert.That(matches.Count, Is.EqualTo(1), cecilifiedCode);
    }
}
