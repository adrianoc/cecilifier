using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class ArrayTests : CecilifierUnitTestBase
{
    [TestCase("string")]
    [TestCase("C")]
    public void TestAccessStringArray(string elementType)
    {
        var result = RunCecilifier($@"class C {{ {elementType} M({elementType} []a) => a[2]; }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(
            cecilifiedCode, 
            Does.Match(
                @"(.+\.Emit\(OpCodes\.)Ldarg_1\);\s+" +
                @"\1Ldc_I4, 2\);\s+" +
                @"\1Ldelem_Ref\);"));
    }
}
