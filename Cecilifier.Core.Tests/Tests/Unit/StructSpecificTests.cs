using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class StructSpecificTests : CecilifierUnitTestBase
{
    [Test]
    public void ReadOnlyStructDeclaration()
    {
        var result = RunCecilifier("readonly struct RO { }");
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(@$"st_rO_\d+\.CustomAttributes\.Add\(new CustomAttribute\(.+""System.Runtime.CompilerServices.IsReadOnlyAttribute"", "".ctor"".+\)\);"));
    }
    
    [TestCase("using System.Runtime.InteropServices; [StructLayout(LayoutKind.Auto, Size = 4)] struct S {}", "AutoLayout", TestName = "AutoLayout")]
    [TestCase("using System.Runtime.InteropServices; [StructLayout(LayoutKind.Explicit, Size = 42)] struct S {}", "ExplicitLayout", TestName = "ExplicitLayout")]
    [TestCase("struct S {}", "SequentialLayout", TestName = "DefaultLayout")]
    public void StructLayoutAttributeIsAdded(string code, string expected)
    {
        var result = RunCecilifier(code);
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(@$"TypeAttributes\.{expected}"));
    }
    
    [Test]
    public void RefStructDeclaration()
    {
        var result = RunCecilifier("ref struct RS { }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match(@"st_rS_\d+\.CustomAttributes\.Add\(new CustomAttribute\(.+""System.Runtime.CompilerServices.IsByRefLikeAttribute"", "".ctor"".+\)\);"));
        Assert.That(cecilifiedCode, Does.Match(@"attr_obsolete_\d+\.ConstructorArguments\.Add\(new CustomAttributeArgument\(.+Boolean, true\)\);"));
        Assert.That(cecilifiedCode, Does.Match(@"st_rS_\d+\.CustomAttributes\.Add\(attr_obsolete_\d+\);"));
    }
}
