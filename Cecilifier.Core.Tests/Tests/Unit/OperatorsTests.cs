using System.Collections.Generic;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class OperatorsTests : CecilifierUnitTestBase
{
    [TestCaseSource(nameof(InequalityOperatorTestScenarios))]
    public void Inequality_Operator(string comparison, string [] expectedCecilifiedExpressions)
    {
        RunTests(comparison, expectedCecilifiedExpressions);
    }

    [TestCaseSource(nameof(EqualityOperatorTestScenarios))]
    public void Equality_Operator(string comparison, string [] expectedCecilifiedExpressions)
    {
        RunTests(comparison, expectedCecilifiedExpressions);
    }

    private static void RunTests(string comparison, string[] expectedCecilifiedExpressions)
    {
        var result = RunCecilifier(
            $"class Foo {{ public static bool operator!=(Foo lhs, Foo rhs) => true; public static bool operator==(Foo lhs, Foo rhs) => false;  bool B(bool b, int i, long l, float f, double d, char ch, string s, Foo foo) => {comparison}; }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        foreach (var expression in expectedCecilifiedExpressions)
        {
            Assert.That(cecilifiedCode, Contains.Substring(expression), cecilifiedCode);
        }
    }
    
    static IEnumerable<TestCaseData> InequalityOperatorTestScenarios()
    {
        yield return new TestCaseData(
            "b != true", 
            new[]
            {
                "il_B_10.Emit(OpCodes.Ldarg_1);",
                "il_B_10.Emit(OpCodes.Ldc_I4, 1);",
                "il_B_10.Emit(OpCodes.Ceq);",
                "il_B_10.Emit(OpCodes.Ldc_I4_0);",
                "il_B_10.Emit(OpCodes.Ceq);",
                "il_B_10.Emit(OpCodes.Ret);",
            }).SetName("Boolean");
        
        yield return new TestCaseData(
            "i != 42", 
            new[]
            {
                "il_B_10.Emit(OpCodes.Ldarg_2);",
                "il_B_10.Emit(OpCodes.Ldc_I4, 42);",
                "il_B_10.Emit(OpCodes.Ceq);",
                "il_B_10.Emit(OpCodes.Ldc_I4_0);",
                "il_B_10.Emit(OpCodes.Ceq);",
                "il_B_10.Emit(OpCodes.Ret);",
            }).SetName("Integer");
        
        yield return new TestCaseData(
            "l != 42", 
            new[]
            {
                "il_B_10.Emit(OpCodes.Ldarg_3);",
                "il_B_10.Emit(OpCodes.Ldc_I4, 42);",
                "il_B_10.Emit(OpCodes.Ceq);",
                "il_B_10.Emit(OpCodes.Ldc_I4_0);",
                "il_B_10.Emit(OpCodes.Ceq);",
                "il_B_10.Emit(OpCodes.Ret);",
            }).SetName("Long");
        
        yield return new TestCaseData(
            "d != 42.2", 
            new[]
            {
                "il_B_10.Emit(OpCodes.Ldarg, 5);",
                "il_B_10.Emit(OpCodes.Ldc_R8, 42.2);",
                "il_B_10.Emit(OpCodes.Ceq);",
                "il_B_10.Emit(OpCodes.Ldc_I4_0);",
                "il_B_10.Emit(OpCodes.Ceq);",
                "il_B_10.Emit(OpCodes.Ret);",
            }).SetName("Double");
        
        yield return new TestCaseData(
            "f != 42.2f", 
            new[]
            {
                "il_B_10.Emit(OpCodes.Ldarg, 4);",
                "il_B_10.Emit(OpCodes.Ldc_R4, 42.2f);",
                "il_B_10.Emit(OpCodes.Ceq);",
                "il_B_10.Emit(OpCodes.Ldc_I4_0);",
                "il_B_10.Emit(OpCodes.Ceq);",
                "il_B_10.Emit(OpCodes.Ret);",
            }).SetName("Float");
        
        yield return new TestCaseData(
            "ch != 'A'",
            new[]
            {
                "il_B_10.Emit(OpCodes.Ldarg, 6);",
                "il_B_10.Emit(OpCodes.Ldc_I4, 65);",
                "il_B_10.Emit(OpCodes.Ceq);",
                "il_B_10.Emit(OpCodes.Ldc_I4_0);",
                "il_B_10.Emit(OpCodes.Ceq);",
                "il_B_10.Emit(OpCodes.Ret);",
            }).SetName("Char");
        
        yield return new TestCaseData(
            "s != \"A\"",
            new []
            { 
                "il_B_10.Emit(OpCodes.Ldarg, 7);",
                "il_B_10.Emit(OpCodes.Ldstr, \"A\");",
                "il_B_10.Emit(OpCodes.Call, assembly.MainModule.ImportReference(TypeHelpers.ResolveMethod(\"System.Private.CoreLib\", \"System.String\", \"op_Inequality\",System.Reflection.BindingFlags.Default|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public,\"\", \"System.String\", \"System.String\")));",
                "il_B_10.Emit(OpCodes.Ret);"   
            }).SetName("String");
        
        yield return new TestCaseData(
            "foo != new Foo()", 
            new []
            { 
                "il_B_10.Emit(OpCodes.Ldarg, 8);",
                "il_B_10.Emit(OpCodes.Newobj, l_foo_19);",
                "il_B_10.Emit(OpCodes.Call, m_op_Inequality_1);",
                "il_B_10.Emit(OpCodes.Ret);"   
            }).SetName("Custom");
    }
    
    static IEnumerable<TestCaseData> EqualityOperatorTestScenarios()
    {
        yield return new TestCaseData(
            "b == true", 
            new[]
            {
                "il_B_10.Emit(OpCodes.Ldarg_1);",
                "il_B_10.Emit(OpCodes.Ldc_I4, 1);",
                "il_B_10.Emit(OpCodes.Ceq);",
            }).SetName("Boolean");
        
        yield return new TestCaseData(
            "i == 42", 
            new[]
            {
                "il_B_10.Emit(OpCodes.Ldarg_2);",
                "il_B_10.Emit(OpCodes.Ldc_I4, 42);",
                "il_B_10.Emit(OpCodes.Ceq);"
            }).SetName("Integer");
        
        yield return new TestCaseData(
            "l == 42", 
            new[]
            {
                "il_B_10.Emit(OpCodes.Ldarg_3);",
                "il_B_10.Emit(OpCodes.Ldc_I4, 42);",
                "il_B_10.Emit(OpCodes.Ceq);",
            }).SetName("Long");
        
        yield return new TestCaseData(
            "d == 42.2", 
            new[]
            {
                "il_B_10.Emit(OpCodes.Ldarg, 5);",
                "il_B_10.Emit(OpCodes.Ldc_R8, 42.2);",
                "il_B_10.Emit(OpCodes.Ceq);",
            }).SetName("Double");
        
        yield return new TestCaseData(
            "f == 42.2f", 
            new[]
            {
                "il_B_10.Emit(OpCodes.Ldarg, 4);",
                "il_B_10.Emit(OpCodes.Ldc_R4, 42.2f);",
                "il_B_10.Emit(OpCodes.Ceq);",
            }).SetName("Float");
        
        yield return new TestCaseData(
            "ch == 'A'",
            new[]
            {
                "il_B_10.Emit(OpCodes.Ldarg, 6);",
                "il_B_10.Emit(OpCodes.Ldc_I4, 65);",
                "il_B_10.Emit(OpCodes.Ceq);",
            }).SetName("Char");
        
        yield return new TestCaseData(
            "s == \"A\"",
            new []
            { 
                "il_B_10.Emit(OpCodes.Ldarg, 7);",
                "il_B_10.Emit(OpCodes.Ldstr, \"A\");",
                "il_B_10.Emit(OpCodes.Call, assembly.MainModule.ImportReference(TypeHelpers.ResolveMethod(\"System.Private.CoreLib\", \"System.String\", \"op_Equality\",System.Reflection.BindingFlags.Default|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public,\"\", \"System.String\", \"System.String\")));",
                "il_B_10.Emit(OpCodes.Ret);"   
            }).SetName("String");
        
        yield return new TestCaseData(
            "foo == new Foo()", 
            new []
            { 
                "il_B_10.Emit(OpCodes.Ldarg, 8);",
                "il_B_10.Emit(OpCodes.Newobj, l_foo_19);",
                "il_B_10.Emit(OpCodes.Call, m_op_Equality_5);",
                "il_B_10.Emit(OpCodes.Ret);"   
            }).SetName("Custom");
    }
}
