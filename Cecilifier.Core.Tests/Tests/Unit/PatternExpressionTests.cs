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
}
