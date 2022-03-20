using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class ForStatementTests : CecilifierUnitTestBase
{
    [TestCase ("parameter++", true)]
    [TestCase("local++", true)]
    [TestCase("field++", true)]
    [TestCase("F()", true)]
    [TestCase ("localDummy = parameter++", false)]
    [TestCase("localDummy = local++", false)]
    [TestCase("localDummy = field++", false)]
    [TestCase("localDummy = F()", false)]
    public void TestForIncrement_IsPopped_IfNotConsumed(string value, bool expectPop)
    {
        var result = RunCecilifier($@"class C {{ int F() => 0; int field; void M(int parameter) {{ int localDummy; int local = parameter; for(int x = 0; x < 1; {value}); }} }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(
            cecilifiedCode, 
            expectPop 
                ? Does.Contain("Pop") 
                : Does.Not.Contains("Pop"));
    }
}
