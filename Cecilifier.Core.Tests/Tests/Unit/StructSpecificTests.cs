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
}
