using System.Collections;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class IsPatternExpressionTests : CecilifierUnitTestBase
{
    [Test]
    public void TestDeclarationPatternSyntax()
    {
        var result = RunCecilifier("void M(object o) { var r = o is string s; }");
        Assert.That(
            result.GeneratedCode.ReadToEnd(), 
            Does.Match(
                @"//var r = o is string s;\s+" +
                @"var l_r_\d+ = new VariableDefinition\(assembly.MainModule.TypeSystem.Boolean\);\s+" +
                @"m_M_\d+.Body.Variables.Add\(l_r_\d+\);\s+" +
                @"(il_M_\d+.Emit\(OpCodes.)Ldarg_1\);\s+" +
                @"var (l_s_\d+) = new VariableDefinition\((.+\.String)\);\s+" +
                @"m_M_\d+.Body.Variables.Add\(\2\);\s+" +
                @"\1Isinst, \3\);\s+" +
                @"\1Stloc, \2\);\s+" +
                @"\1Ldloc, \2\);\s+" +
                @"\1Ldnull\);\s+" +
                @"\1Cgt\);\s+" +
                @"\1Stloc, l_r_\d+\);"));
    }
    
    [Test]
    public void TestDeclarationPatternSyntaxWithGenerics()
    {
        var result = RunCecilifier("void M<T>(T o) { var r = o is string s; }");
        Assert.That(
            result.GeneratedCode.ReadToEnd(), 
            Does.Match(
                @"//var r = o is string s;\s+" +
                @"var l_r_\d+ = new VariableDefinition\(assembly.MainModule.TypeSystem.Boolean\);\s+" +
                @"m_M_\d+.Body.Variables.Add\(l_r_\d+\);\s+" +
                @"(il_M_\d+.Emit\(OpCodes.)Ldarg_1\);\s+" +
                @"\1Box, gp_T_\d+\);\s+" +
                @"var (l_s_\d+) = new VariableDefinition\((.+\.String)\);\s+" +
                @"m_M_\d+.Body.Variables.Add\(\2\);\s+" +
                @"\1Isinst, \3\);\s+" +
                @"\1Stloc, \2\);\s+" +
                @"\1Ldloc, \2\);\s+" +
                @"\1Ldnull\);\s+" +
                @"\1Cgt\);\s+" +
                @"\1Stloc, l_r_\d+\);"));
    }
    
    [TestCaseSource(nameof(RecursivePatternsTestScenarios))]
    public void TestRecursivePatternSyntax(string code, string expected)
    {
        var result = RunCecilifier(code);
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expected));
    }

    private static IEnumerable RecursivePatternsTestScenarios()
    {
        var singlePropertyExpectation = @"//var r = o is string { Length: 10 } s;\s+" +
                                        @"var (l_r_\d+) = new VariableDefinition\(assembly.MainModule.TypeSystem.Boolean\);\s+" +
                                        @"m_M_\d+.Body.Variables.Add\(\1\);\s+" +
                                        @"(il_M_\d+).Emit\(OpCodes.Ldarg_1\);\s+" +
                                        @"var (l_s_\d+) = new VariableDefinition\(assembly.MainModule.TypeSystem.String\);\s+" +
                                        @"m_M_6.Body.Variables.Add\(\3\);\s+" +
                                        @"var ldc_I4_0_\d+ = \2.Create\(OpCodes.Ldc_I4_0\);\s+" +
                                        @"var (nop_\d+) = \2.Create\(OpCodes.Nop\);\s+" +
                                        @"(\2.Emit\(OpCodes\.)Isinst, assembly.MainModule.TypeSystem.String\);\s+" +
                                        @"\5Stloc, \3\);\s+" +
                                        @"\5Ldloc, \3\);\s+" +
                                        @"\5Brfalse_S, ldc_I4_0_\d+\);\s+" +
                                        @"\5Ldloc, \3\);\s+" +
                                        @"\5Callvirt, .+""System.String"", ""get_Length"".+\);\s+" +
                                        @"\5Ldc_I4, 10\);\s+" +
                                        @"\5Bne_Un, ldc_I4_0_\d+\);\s+" +
                                        @"\5Ldc_I4_1\);\s+" +
                                        @"\5Br_S, \4\);\s+" +
                                        @"\2.Append\(ldc_I4_0_\d+\);\s+" +
                                        @"\2.Append\(\4\);\s+" +
                                        @"\5Stloc, \1\);";
        
        yield return new TestCaseData("void M(object o) { var r = o is string { Length: 10 } s; }", singlePropertyExpectation) { TestName = "Single Property" };

        yield return new TestCaseData(
            "void M(object o) { var r = o is string { Length: 10 }; }",
            singlePropertyExpectation
                .Replace("l_s_", "l_tmp_") // since we are not capturing the result of the pattern, Cecilifier will introduce a temporary variable.
                .Replace("10 } s;", "10 };")) // Accounts for the commented expression in the output which does not capture the `s` variable.
        {
            TestName = "Single Property (do not capture)"
        };
        
        yield return new TestCaseData(
            "void M(object o) { var r = o is System.Uri { IsDefaultPort: false, Host: \"bar\", Port: 42 }; }",

            @"(il_M_\d+\.Emit\(OpCodes\.)Isinst, .+System.Uri.+\);\s+" +
            @"\1Stloc, (l_tmp_\d+)\);\s+" +
            @"\1Ldloc, \2\);\s+" +
            @"\1Brfalse_S, (ldc_I4_0_\d+)\);\s+" +
            @"\1Ldloc, \2\);\s+" +
            @"\1Callvirt, .+System.Uri.+, ""get_IsDefaultPort"",.+\);\s+" +
            @"\1Ldc_I4, 0\);\s+" +
            @"\1Bne_Un, \3\);\s+" +
            @"\1Ldloc, \2\);\s+" +
            @"\1Callvirt, .+System.Uri.+""get_Host"".+\);\s+" +
            @"\1Ldstr, ""bar""\);\s+" +
            @"\1Call, .+System.String.+, ""op_Equality"".+\);\s+" +
            @"\1Brfalse_S, \3\);\s+" +
            @"\1Ldloc, \2\);\s+" +
            @"\1Callvirt, .+System.Uri.+, ""get_Port"".+\);\s+" +
            @"\1Ldc_I4, 42\);\s+" +
            @"\1Bne_Un, \3\);\s+" +
            @"\1Ldc_I4_1\);\s+" +
            @"\1Br_S, nop_12\);\s+" +
            @"il_M_7.Append\(\3\);\s+" +
            @"il_M_7.Append\(nop_12\);\s+" +
            @"\1Stloc, l_r_9\);") { TestName = "Multiple Properties" };
    }
}
