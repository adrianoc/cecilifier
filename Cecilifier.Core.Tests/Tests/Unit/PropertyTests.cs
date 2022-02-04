using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class PropertyTests : CecilifierUnitTestBase
{
    [Test]
    public void TestGetterOnlyInitialization_Simple()
    {
        var result = RunCecilifier("class C { public int Value { get; } public C() => Value = 42; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Contains.Substring(
            @"il_ctor_6.Append(ldarg_0_7);
			il_ctor_6.Emit(OpCodes.Ldc_I4, 42);
			il_ctor_6.Emit(OpCodes.Stfld, fld_value_4);"));
    }
    
    [Test]
    public void TestGetterOnlyInitialization_Complex()
    {
        var result = RunCecilifier("class C { public int Value { get; } public C(int n) => Value = n * 2; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Contains.Substring(
            @"il_ctor_6.Append(ldarg_0_8);
			il_ctor_6.Emit(OpCodes.Ldarg_1);
			il_ctor_6.Emit(OpCodes.Ldc_I4, 2);
			il_ctor_6.Emit(OpCodes.Mul);
			il_ctor_6.Emit(OpCodes.Stfld, fld_value_4);"));
    }
    
    [Test]
    public void TestAutoPropertyWithGetterAndSetter()
    {
        var result = RunCecilifier("class C { public int Value { get; set; } public C() => Value = 42; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Contains.Substring(
            @"il_ctor_8.Emit(OpCodes.Ldc_I4, 42);
			il_ctor_8.Emit(OpCodes.Call, l_set_5);"));
    }
}
