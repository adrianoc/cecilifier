using System;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class LocalFunctionTests : CecilifierUnitTestBase
{
    [Test]
    public void InTopLevel_Statements([Values("static", "")] string staticOrInstance)
    {
        var result = RunCecilifier($"{staticOrInstance} int LocalFoo() => 42; System.Console.WriteLine(LocalFoo());");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(
            cecilifiedCode,
            Does.Match(@"var m_localFoo_\d+ = new MethodDefinition\(""<<Main>\$>g__LocalFoo\|0_0\"", .+, assembly.MainModule.TypeSystem.Int32\);"));

        // asserts that il_topLevelMain_3 is the variable holding the ILProcessor for the top level statement body.
        Assert.That(cecilifiedCode, Contains.Substring("var il_topLevelMain_3 = m_topLevelStatements_1.Body.GetILProcessor();"), cecilifiedCode);

        Assert.That(
            cecilifiedCode,
            Does.Not.Match(@"il_topLevelMain_3.Emit\(OpCodes.Ldarg_0\);"),
            "Looks like local function is being handled as instance method, instead of a static one.");
    }

    [Test]
    public void InTopLevel_Statements_ForwardReferenced([Values("static", "")] string staticOrInstance)
    {
        var result = RunCecilifier($"System.Console.WriteLine(LocalFoo()); {staticOrInstance} int LocalFoo() => 42;");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(
            cecilifiedCode,
            Contains.Substring("var m_localFoo_6 = new MethodDefinition(\"<<Main>$>g__LocalFoo|0_0\", MethodAttributes.Private, assembly.MainModule.TypeSystem.Int32);"));

        // asserts that il_topLevelMain_3 is the variable holding the ILProcessor for the top level statement body.
        Assert.That(cecilifiedCode, Contains.Substring("var il_topLevelMain_3 = m_topLevelStatements_1.Body.GetILProcessor();"));

        Assert.That(
            cecilifiedCode,
            Does.Not.Match(@"il_topLevelMain_3.Emit\(OpCodes.Ldarg_0\);"),
            "Looks like local function is being handled as instance method, instead of a static one.");
    }

    [Test]
    public void InTopLevel_Class([Values("static", "")] string staticOrInstance)
    {
        var result = RunCecilifier($"class Bar {{ void Normal() {{ {staticOrInstance} int LocalFoo() => 42; System.Console.WriteLine(LocalFoo()); }} }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(
            cecilifiedCode,
            Does.Match("var m_localFoo_3 = new MethodDefinition\\(\"<Normal>g__LocalFoo|0_0\", MethodAttributes.Assembly \\| MethodAttributes.Static \\| MethodAttributes.HideBySig, .*\\);"));

        Assert.That(
            cecilifiedCode,
            Does.Not.Match(@"l_normal_2.Emit\(OpCodes.Ldarg_0\);"),
            "Looks like local function is being handled as instance method, instead of a static one.");

        Assert.That(
            cecilifiedCode,
            Does.Match(@"//System.Console.WriteLine\(LocalFoo\(\)\);\s+il_normal_2.Emit\(OpCodes.Call, m_localFoo_3\);"));
    }

    [TestCase("""
              M(10);
              void M<T>(T t) => System.Console.WriteLine(t);
              """, Description = "Issue=#268", TestName = "Top level")]
    
    [TestCase("""
              class Foo
              {
                 void Call()
                 {
                    M(10);
                    void M<T>(T t) => System.Console.WriteLine(t);
                 }
              }
              """, Description = "Issue=#268", TestName = "Forward reference in method")]
    public void GenericLocalFunctions_DoesNotReferencesTypeParameters_ThroughReflection(string snippetToTest)
    {
        var result = RunCecilifier(snippetToTest);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilifiedCode, Does.Not.Contain("typeof(T)"));

        Assert.Multiple(() =>
        {
            Assert.That(cecilifiedCode, Does.Match("""
                                                   \s+var (p_t_\d+) = new ParameterDefinition\("t", ParameterAttributes.None, gp_T_\d+\);
                                                   \s+m_M_\d+.Parameters.Add\(\1\);
                                                   """));

            Assert.That(cecilifiedCode, Does.Match(@"il_M_\d+.Emit\(OpCodes.Box, gp_T_\d+\);"));
        });
    }
    
    [Test]
    public void Instanceness_IsRespected([Values] bool staticOrInstance)
    {
        var modifier = staticOrInstance ? "static" : string.Empty;
        
        var result = RunCecilifier($"{modifier} int LocalFoo(int i) => i; System.Console.WriteLine(LocalFoo(42));");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var declarationModifier = staticOrInstance ? "MethodAttributes.Static | " : String.Empty;
        Assert.That(
            cecilifiedCode,
            Contains.Substring($"var m_localFoo_6 = new MethodDefinition(\"<<Main>$>g__LocalFoo|0_0\", MethodAttributes.Assembly | {declarationModifier}MethodAttributes.HideBySig, assembly.MainModule.TypeSystem.Int32);"),
            cecilifiedCode);

        var loadArgOpCode = staticOrInstance ? "Ldarg_0" : "Ldarg_1";
        Assert.That(
            cecilifiedCode,
            Does.Match(@$"il_localFoo_\d+.Emit\(OpCodes.{loadArgOpCode}\);"),
            $"Expected {loadArgOpCode} not found (looks like local function static modifier is being mishandled).");
    }
}
