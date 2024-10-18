using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class MiscellaneousMemberTests : CecilifierUnitTestBase
{
    [TestCase("class Foo { void M() => _field = 0; int _field; }", @"il_M_\d+.Emit\(OpCodes.Stfld, fld__field_\d+\)", TestName = "Field")]
    [TestCase("class Foo { void M() =>  Property = 0; int Property {get; set; } }", @"il_M_\d+.Emit\(OpCodes.Call, l_set_\d+\);", TestName = "Property")]
    public void ForwardMemberAssignment(string source, string expectedRegEx)
    {
        var result = RunCecilifier(source);
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expectedRegEx));
    }

    [TestCase("[System.Runtime.CompilerServices.SkipLocalsInit] void Method() { int i; }", TestName = "Local function in global statement")]
    [TestCase("void Foo() { [System.Runtime.CompilerServices.SkipLocalsInit] void Method() { int i; } }", TestName = "Local function")]
    [TestCase("class Foo { [System.Runtime.CompilerServices.SkipLocalsInit] void Method() { int i; } }", TestName = "Member method")]
    public void SkipLocalsInitAttribute_IsRespected(string snippet)
    {
        var result = RunCecilifier(snippet);
        
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(@"m_method_\d+\.Body.InitLocals = false;"));
    }
}
