using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class DefaultExpressions : CecilifierUnitTestBase
{
    private const string DefaultTypeParameterExpectation = @"il_M_\d+.Emit\(OpCodes.Ldloca_S, l_T_\d+\);\s+" +
                                                           @"il_M_\d+.Emit\(OpCodes.Initobj, gp_T_\d+\);";

    private const string PtrExpectation = @"il_M_\d+.Emit\(OpCodes.Ldc_I4_0\);\s+" + 
                                          @"il_M_\d+.Emit\(OpCodes.Conv_U\);";
    
    private const string IntPtrExpectation = @"il_M_\d+.Emit\(OpCodes.Ldc_I4, 0\);\s+" + 
                                            @"il_M_\d+.Emit\(OpCodes.Conv_I\);";
    
    private const string NumericPrimitiveExpectation = @"il_M_\d+.Emit\(OpCodes.Ldc_I4, 0\);";
    private const string ReferenceTypeExpectation = @"il_M_\d+\.Emit\(OpCodes.Ldnull\)";
        
    [TestCase("class Foo<T> { T M() => default; }", DefaultTypeParameterExpectation, TestName = "Literal Unconstrained Type Parameter")]
    [TestCase("class Foo<T> { T M() => default(T); }", DefaultTypeParameterExpectation, TestName = "Type Unconstrained Parameter")]
    [TestCase("class Foo<T> where T : class { T M() => default(T); }", DefaultTypeParameterExpectation, TestName = "Type Parameter (class)")]
    [TestCase("class Foo<T> where T : struct { T M() => default(T); }", DefaultTypeParameterExpectation, TestName = "Type Parameter (struct)")]
    [TestCase("T M<T>() => default;", DefaultTypeParameterExpectation, TestName = "Toplevel Literal Unconstrained Type Parameter")]
    [TestCase("T M<T>() => default(T);", DefaultTypeParameterExpectation, TestName = "Toplevel Unconstrained Type Parameter")]
    [TestCase("int M() => default;", NumericPrimitiveExpectation, TestName = "Toplevel Literal Primitive")]
    [TestCase("int M() => default(int);", NumericPrimitiveExpectation, TestName = "Toplevel Primitive")]
    [TestCase("System.IntPtr M() => default;", IntPtrExpectation, TestName = "Toplevel Literal IntPtr")]
    [TestCase("System.IntPtr M() => default(System.IntPtr);", IntPtrExpectation, TestName = "Toplevel IntPtr")]
    [TestCase("unsafe int* M() => default;", @PtrExpectation, TestName = "Toplevel Literal Ptr")]
    [TestCase("unsafe int* M() => default(int *);", PtrExpectation, TestName = "Toplevel Ptr")]
    [TestCase("string M() => default;", @"Emit\(OpCodes.Ldnull\)", TestName = "Toplevel Literal External Class")]
    [TestCase("string M() => default(string);", ReferenceTypeExpectation, TestName = "Toplevel External Class")]
    [TestCase("class Foo { Foo M() => default; }", ReferenceTypeExpectation, TestName = "Literal Class")]
    [TestCase("class Foo { Foo M() => default(Foo); }", ReferenceTypeExpectation, TestName = "Class")]
    [TestCase("string M() => default(int).ToString();", @"(il_M_\d+\.Emit\(OpCodes\.)Stloc, (l_tmp_\d+)\);\s+"+ 
                                                                    @"\1Ldloca_S, \2\);", TestName = "Invocation On Default")]
    [TestCase("void M(int p = default) {}", @"p_p_\d+.Constant = 0;", TestName = "Literal Int Parameter")]
    [TestCase("void M(double p = default) {}", @"p_p_\d+.Constant = 0.0D;", TestName = "Literal Double Parameter")]
    [TestCase("void M(int p = default(int)) {}", @"p_p_\d+.Constant = 0;", TestName = "Int Parameter")]
    [TestCase("void M() { System.Action a = default; }", ReferenceTypeExpectation, TestName = "Literal Delegate")]
    [TestCase("void M() { System.Action a = default(System.Action); }", ReferenceTypeExpectation, TestName = "Delegate")]
    public void Test(string code, string expected)
    {
        var result = RunCecilifier(code);
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expected));
    }
}
