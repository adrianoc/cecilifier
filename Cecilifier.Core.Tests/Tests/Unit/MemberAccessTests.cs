using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class MemberAccessTests : CecilifierUnitTestBase
{
    [TestCase("p", TestName = "Parameter")]
    [TestCase("lr", TestName = "Local")]
    public void TestRefTarget(string target)
    {
        var result = RunCecilifier($@"class Foo {{ int value; void Bar(ref Foo p)  {{ ref Foo lr = ref p; {target}.value = 42; }} }}");
        Assert.That(
            result.GeneratedCode.ReadToEnd(), 
            Does.Match(
                @"(?<il>il_bar_\d+\.Emit\(OpCodes.)(?:Ldarg_1|Ldloc,.+)\);\s+" +
			        @"\k<il>Ldind_Ref\);\s+" +
                    @"\k<il>Ldc_I4, 42\);\s+" +
		            @"\k<il>Stfld, fld_value_1\);"));
    }
}
