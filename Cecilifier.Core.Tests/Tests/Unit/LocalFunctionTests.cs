using Cecilifier.Core.Tests.Tests.Unit.Framework;
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
            Contains.Substring("var m_localFoo_6 = new MethodDefinition(\"<<Main>$>g__LocalFoo|0_0\", MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig, assembly.MainModule.TypeSystem.Int32);"),
            cecilifiedCode);

        // asserts that il_topLevelMain_3 is the variable holding the ILProcessor for the top level statement body.
        Assert.That(cecilifiedCode, Contains.Substring("var il_topLevelMain_4 = m_topLevelMain_3.Body.GetILProcessor();"), cecilifiedCode);

        Assert.That(
            cecilifiedCode,
            Does.Not.Match(@"il_topLevelMain_\d+.Emit\(OpCodes.Ldarg_0\);"),
            "Looks like local function is being handled as instance method, instead of a static one.");
    }

    [Test]
    public void InTopLevel_Statements_ForwardReferenced([Values("static", "")] string staticOrInstance)
    {
        var result = RunCecilifier($"System.Console.WriteLine(LocalFoo()); {staticOrInstance} int LocalFoo() => 42;");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        var expectedAttributes = staticOrInstance == "static" 
                                        ? "MethodAttributes.Private | MethodAttributes.Static" 
                                        : "MethodAttributes.Private";
        Assert.That(
            cecilifiedCode,
            Contains.Substring($"var m_localFoo_6 = new MethodDefinition(\"<<Main>$>g__LocalFoo|0_0\", {expectedAttributes}, assembly.MainModule.TypeSystem.Int32);"));

        // asserts that il_topLevelMain_3 is the variable holding the ILProcessor for the top level statement body.
        Assert.That(cecilifiedCode, Contains.Substring("var il_topLevelMain_4 = m_topLevelMain_3.Body.GetILProcessor();"));

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
}
