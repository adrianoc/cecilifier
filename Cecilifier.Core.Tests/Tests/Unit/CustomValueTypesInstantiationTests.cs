using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class CustomValueTypesInstantiationTests : CecilifierUnitTestBase
{
    [Test]
    public void ParameterAssignment()
    {
        var result = RunCecilifier(@"struct MyStruct { } class Foo { void M(MyStruct m) { m = new MyStruct(); } }");
        Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring(
            @"il_M_3.Emit(OpCodes.Ldarga, 0);
			il_M_3.Emit(OpCodes.Initobj, st_myStruct_0);"));
    }
    
    [TestCase("out")]
    [TestCase("ref")]
    public void OutRefParameterAssignment(string outRefModifier)
    {
        var result = RunCecilifier($@"struct MyStruct {{ }} class Foo {{ void M({outRefModifier} MyStruct m) {{ m = new MyStruct(); }} }}");
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(
            @"il_M_3.Emit\(OpCodes.Ldarg_1\);\s+" +
                @"il_M_3.Emit\(OpCodes.Initobj, st_myStruct_0\);"));
    } 
    
    [TestCase("p")]
    [TestCase("default(MyStruct)", IgnoreReason = "default() not supported")]
    public void SimpleStoreToOutStructParameter(string toStore)
    {
        var result = RunCecilifier($@"struct MyStruct {{ }} class Foo {{ void M(out MyStruct m, MyStruct p) {{ m = {toStore}; }} }}");
        Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring(
            @"il_M_3.Emit(OpCodes.Ldarg_1);
			il_M_3.Emit(OpCodes.Ldarg_2);
			il_M_3.Emit(OpCodes.Stobj);"));
    }
    
    [Test]
    public void LocalAssignment()
    {
        var result = RunCecilifier(@"struct MyStruct { } class Foo { void M() { MyStruct m; m = new MyStruct(); } }");
        Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring(
            @"il_M_3.Emit(OpCodes.Ldloca_S, l_m_4);
			il_M_3.Emit(OpCodes.Initobj, st_myStruct_0);"));
    }
    
    [Test]
    public void RefLocalAssignment()
    {
        var result = RunCecilifier(@"struct MyStruct { } class Foo { void M(MyStruct p) { ref MyStruct l = ref p; l = new MyStruct(); } }");
        Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring(
            @"	il_M_3.Emit(OpCodes.Ldloc_S, l_l_5);
			il_M_3.Emit(OpCodes.Initobj, st_myStruct_0);"));
    }

    [TestCase("p")]
    [TestCase("f")]
    public void ArrayElementAssignment(string arrayVariable)
    {
        var result = RunCecilifier($@"struct MyStruct {{ }} class Foo {{ MyStruct []f; void M(MyStruct []p) {{ {arrayVariable}[1] = new MyStruct(); }} }}");
        Assert.That(
            result.GeneratedCode.ReadToEnd(), Does.Match(
            @"il_M_4.Emit\(OpCodes.(?:Ldarg_1|Ldfld, fld_f_2)\);
			il_M_4.Emit\(OpCodes.Ldc_I4, 1\);
			il_M_4.Emit\(OpCodes.Ldelema\);
			il_M_4.Emit\(OpCodes.Initobj, st_myStruct_0\);"));
    }
    
    [Test]
    public void TestParameterlessStructInstantiation()
    {
        var result = RunCecilifier("struct Foo { static Foo Create() => new Foo(); public Foo() { } }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(@"il_create_2\.Emit\(OpCodes.Newobj, ctor_foo_3\);"));
    }    
}
