using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class MiscellaneousMemberTests : CecilifierUnitTestBase
{
    [TestCase("class Foo { void M() => _field = 0; int _field; }", @"il_M_\d+.Emit\(OpCodes.Stfld, fld__field_\d+\)",TestName = "Field")]
    [TestCase("class Foo { void M() =>  Property = 0; int Property {get; set; } }", @"il_M_\d+.Emit\(OpCodes.Call, l_set_\d+\);", TestName = "Property")]
    public void ForwardMemberAssignment(string source, string expectedRegEx)
    {
        var result = RunCecilifier(source);
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expectedRegEx));
    }
}
