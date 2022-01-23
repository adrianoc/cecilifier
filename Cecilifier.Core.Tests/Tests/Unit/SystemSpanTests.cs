using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class SystemSpanTests : CecilifierUnitTestBase
{
    [TestCase("using System; class C { int M(Span<int> a, Index index) => a[index]; }", TestName = "Parameter")]
    [TestCase("using System; class C { static Index index; int M(Span<int> a) => a[index]; }", TestName = "Field")]
    [TestCase("using System; class C { int M(Span<int> a) { Index index; index = 1; return a[index]; } }", TestName = "Local")]
    public void IndexUsedToIndexSpan_SpanLength_IsCalled(string code)
    {
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(
            @"(il_M_\d.Emit\(OpCodes.)Ldarga, p_a_(\d+)\);\s+" +
            @"\1(Ldloca|Ldarga|Ldflda|Ldsflda), (p|l|fld)_index_\d\);\s+" +
            @"\1Ldarga, p_a_\2\);\s+" +
            @"il_M_\d.Emit\(OpCodes.Call.+get_Length.+\);\s+" +
            @"il_M_\d.Emit\(OpCodes.Conv_I4\);\s+" +
            @"il_M_\d.Emit\(OpCodes.Call, .+GetOffset.+\);\s+"));
    }

    [Test]
    public void ReferenceToLength()
    {
        var result = RunCecilifier("class Foo { int M(System.Span<char> s) => s.Length; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(@"il_M_\d.Emit\(OpCodes.Call.+get_Length.+\);"));
    }
}
