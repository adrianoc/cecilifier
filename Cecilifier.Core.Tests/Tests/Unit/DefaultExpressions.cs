using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class DefaultExpressions : CecilifierUnitTestBase
{
    private const string DefaultTypeParameterMethodInvocationExpectation = """
                                                                           var (l_T_\d+) = new VariableDefinition\((gp_T_\d+)\);
                                                                           \s+m_M_\d+.Body.Variables.Add\(\1\);
                                                                           (\s+il_M_\d+\.Emit\(OpCodes\.)Ldloca_S, \1\);
                                                                           \3Initobj, \2\);
                                                                           \3Ldloca_S, \1\);
                                                                           \3Constrained, \2\);
                                                                           \3Callvirt, .+ImportReference\(.+ResolveMethod\(typeof\(System.Object\), "GetHashCode",.+\)\);
                                                                           """;
    
    private const string DefaultTypeParameterMethodInvocationOnReferenceTypeExpectation = """
                                                                           var (l_T_\d+) = new VariableDefinition\((gp_T_\d+)\);
                                                                           \s+m_M_\d+.Body.Variables.Add\(\1\);
                                                                           (\s+il_M_\d+\.Emit\(OpCodes\.)Ldloca_S, \1\);
                                                                           \3Initobj, \2\);
                                                                           \3Ldloc, \1\);
                                                                           \3Box, \2\);
                                                                           \3Callvirt, .+ImportReference\(.+ResolveMethod\(typeof\(System.Object\), "GetHashCode",.+\)\);
                                                                           """;
    
    private const string DefaultTypeParameterInParameterAssignmentExpectation = """
                                                           \s+il_M_\d+.Emit\(OpCodes.Ldarg_S, p_t_\d+\);
                                                           \s+il_M_\d+.Emit\(OpCodes.Initobj, gp_T_\d+\);
                                                           \s+il_M_\d+.Emit\(OpCodes.Ret\);
                                                           """;
    
    private const string DefaultTypeParameterInLocalVariableInitializationExpectation = """
                                                           \s+var (l_t_\d+) = new VariableDefinition\((gp_T_\d+)\);
                                                           \s+m_M_\d+.Body.Variables.Add\(\1\);
                                                           \s+il_M_\d+.Emit\(OpCodes.Ldloca_S, \1\);
                                                           \s+il_M_\d+.Emit\(OpCodes.Initobj, \2\);
                                                           \s+il_M_\d+.Emit\(OpCodes.Ret\);
                                                           """;
    
    private const string BaseDefaultTypeParameterInLocalVariableAssignmentExpectation = """
                                                           \s+il_M_\d+.Emit\(OpCodes.Ldloca_S, l_t_\d+\);
                                                           \s+il_M_\d+.Emit\(OpCodes.Initobj, gp_T_\d+\);
                                                           \s+il_M_\d+.Emit\(OpCodes.Ret\);
                                                           """;
    
    private const string DefaultTypeParameterInLocalVariableAssignmentExpectation = $"""
                                                           //t = default\(T\);
                                                           {BaseDefaultTypeParameterInLocalVariableAssignmentExpectation}
                                                           """;

    private const string DefaultLiteralTypeParameterInLocalVariableAssignmentExpectation = $"""
                                                           //t = default;
                                                           {BaseDefaultTypeParameterInLocalVariableAssignmentExpectation}
                                                           """;

    private const string DefaultTypeParameterExpectation = """
                                                           \s+m_M_\d+.Body.Variables.Add\((l_T_\d+)\);
                                                           \s+il_M_\d+.Emit\(OpCodes.Ldloca_S, \1\);
                                                           \s+il_M_\d+.Emit\(OpCodes.Initobj, gp_T_\d+\);
                                                           \s+il_M_\d+.Emit\(OpCodes.Ldloc, \1\);
                                                           \s+il_M_\d+.Emit\(OpCodes.Ret\);
                                                           """;

    private const string PtrExpectation = @"il_M_\d+.Emit\(OpCodes.Ldc_I4_0\);\s+" +
                                          @"il_M_\d+.Emit\(OpCodes.Conv_U\);";

    private const string IntPtrExpectation = @"il_M_\d+.Emit\(OpCodes.Ldc_I4, 0\);\s+" +
                                            @"il_M_\d+.Emit\(OpCodes.Conv_I\);";

    private const string NumericPrimitiveExpectation = @"il_M_\d+.Emit\(OpCodes.Ldc_I4, 0\);";
    private const string ReferenceTypeExpectation = @"il_M_\d+\.Emit\(OpCodes.Ldnull\)";

    [TestCase("class Foo<T> { T M() => default; }", DefaultTypeParameterExpectation, TestName = "Literal Unconstrained Type Parameter")]
    [TestCase("class Foo<T> { T M() => default(T); }", DefaultTypeParameterExpectation, TestName = "Type Unconstrained Parameter")]
    [TestCase("void M<T>() { T t = default(T); }", DefaultTypeParameterInLocalVariableInitializationExpectation, TestName = "Type Parameter Local Variable initialization")]
    [TestCase("void M<T>(T t) { t = default(T); }", DefaultTypeParameterInParameterAssignmentExpectation, TestName = "Type Parameter parameter assignment")]
    [TestCase("void M<T>() { T t; t = default(T); }", DefaultTypeParameterInLocalVariableAssignmentExpectation, TestName = "Type Parameter Local Variable assignment")]
    [TestCase("void M<T>() { T t; t = default; }", DefaultLiteralTypeParameterInLocalVariableAssignmentExpectation, TestName = "Type Parameter Local Variable assignment default literal")]
    [TestCase("class Foo<T> where T : class { T M() => default(T); }", DefaultTypeParameterExpectation, TestName = "Type Parameter (class)")]
    [TestCase("class Foo<T> where T : struct { T M() => default(T); }", DefaultTypeParameterExpectation, TestName = "Type Parameter (struct)")]
    [TestCase("T M<T>() => default;", DefaultTypeParameterExpectation, TestName = "Toplevel Literal Unconstrained Type Parameter")]
    [TestCase("T M<T>() => default(T);", DefaultTypeParameterExpectation, TestName = "Toplevel Unconstrained Type Parameter")]
    [TestCase("void M<T>() where T : class { var hc = default(T).GetHashCode(); }", DefaultTypeParameterMethodInvocationOnReferenceTypeExpectation, TestName = "Toplevel invocation on default(Class Constrained Type Parameter)")]
    [TestCase("void M<T>() where T : Foo { var hc = default(T).GetHashCode(); } class Foo{} ", DefaultTypeParameterMethodInvocationOnReferenceTypeExpectation, TestName = "Toplevel invocation on default(Reference Type Constrained Type Parameter)")]
    [TestCase("void M<T>() { var hc = default(T).GetHashCode(); }", DefaultTypeParameterMethodInvocationExpectation, TestName = "Toplevel invocation on default(Unconstrained Type Parameter)")]
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
    [TestCase("string M() => default(int).ToString();", @"(il_M_\d+\.Emit\(OpCodes\.)Stloc, (l_tmp_\d+)\);\s+" +
                                                                    @"\1Ldloca_S, \2\);", TestName = "Invocation On Default")]
    [TestCase("void M(int p = default) {}", @"p_p_\d+.Constant = 0;", TestName = "Literal Int Parameter")]
    [TestCase("void M(double p = default) {}", @"p_p_\d+.Constant = 0d;", TestName = "Literal Double Parameter")]
    [TestCase("void M(int p = default(int)) {}", @"p_p_\d+.Constant = 0;", TestName = "Int Parameter")]
    [TestCase("void M() { System.Action a = default; }", ReferenceTypeExpectation, TestName = "Literal Delegate")]
    [TestCase("void M() { System.Action a = default(System.Action); }", ReferenceTypeExpectation, TestName = "Delegate")]
    public void Test(string code, string expected)
    {
        var result = RunCecilifier(code);
        var actual = result.GeneratedCode.ReadToEnd();
        Assert.That(actual, Does.Match(expected));
    }

    [TestCase("char", "Emit(OpCodes.Ldc_I4, 0)")]
    [TestCase("byte", "Emit(OpCodes.Ldc_I4, 0)")]
    [TestCase("sbyte", "Emit(OpCodes.Ldc_I4, 0)")]
    [TestCase("int", "Emit(OpCodes.Ldc_I4, 0)")]
    [TestCase("uint", "Emit(OpCodes.Ldc_I4, 0)")]
    [TestCase("long", "Emit(OpCodes.Ldc_I8, 0L)")]
    [TestCase("ulong", "Emit(OpCodes.Ldc_I8, 0L)")]
    [TestCase("float", "Emit(OpCodes.Ldc_R4, 0.0F)")]
    [TestCase("double", "Emit(OpCodes.Ldc_R8, 0.0D)")]
    [TestCase("bool", "Emit(OpCodes.Ldc_I4, 0)")]
    [TestCase("short", "Emit(OpCodes.Ldc_I4, 0)")]
    [TestCase("ushort", "Emit(OpCodes.Ldc_I4, 0)")]
    [TestCase("System.IntPtr", "Emit(OpCodes.Conv_I)")]
    [TestCase("System.UIntPtr", "Emit(OpCodes.Conv_I)")]
    [TestCase("T", "Emit(OpCodes.Initobj,")]
    public void TestDefaultLiteralExpression(string type, string expected)
    {
        var result = RunCecilifier($"class C<T> {{ {type} v = default; }}");
        Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring(expected));
    }

    [TestCase("System.DateTime v = default;")]
    [TestCase("Foo v = default; struct Foo {}", TestName = "Custom struct (variable declaration)")]
    [TestCase("Foo v; v = default; struct Foo {}", TestName = "Custom struct (simple assignment)")]
    public void TestDefaultLiteralExpressionWithStructs(string code)
    {
        var expected = """
                    \s+//(?:.+ v = default)|(?:Foo v);
                    \s+var (l_v_\d+) = new VariableDefinition\((.+)\);
                    \s+m_topLevelStatements_\d+.Body.Variables.Add\(\1\);
                    (?:\s+//v = default;)?
                    \s+(il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldloca, l_v_\d+\);
                    \s+\3Initobj, \2\);
                    """;

        var cecilifiedCode = RunCecilifier(code).GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(expected));
        Assert.That(cecilifiedCode, Does.Not.Match(@"Stloc, l_v_\d+"));
    }

    // default literal expressions in parameters are tested in Parameters tests.
}
