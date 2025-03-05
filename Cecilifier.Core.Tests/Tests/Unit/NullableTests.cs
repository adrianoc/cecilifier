using System.Text.RegularExpressions;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class NullableTests : CecilifierUnitTestBase
{
    [TestCase("object", TestName = "object")]
    [TestCase("Foo", TestName = "Foo")]
    [TestCase("IFoo", TestName = "IFoo")]
    [TestCase("int", TestName = "int")]
    public void MethodsWithNullableParameters_AreRegistered_Once(string parameterType)
    {
        var result = RunCecilifier(
            $$"""
              interface IFoo {} 
              class Foo : IFoo 
              { 
                 void M({{parameterType}}? o) {} 
                 void M2({{parameterType}} p) => M(p);
              }
              """);
        
        var actual = result.GeneratedCode.ReadToEnd();
        
        Assert.That(
            Regex.Count(actual, """var .+ = new MethodDefinition\("M", MethodAttributes.Private \| MethodAttributes.HideBySig, assembly.MainModule.TypeSystem.Void\);\n"""), 
            Is.EqualTo(1), 
            "Only only declaration for method M() is expected.");
    }
    
    [TestCase("object", @".+TypeSystem\.Object",  TestName = "object")]
    [TestCase("Foo", "cls_foo_1",  TestName = "Foo")]
    [TestCase("IFoo", "itf_iFoo_0", TestName = "IFoo")]
    [TestCase("int", @".+ImportReference\(typeof\(System.Nullable<>\)\).MakeGenericInstanceType\(.+Int32\)", TestName = "int")]
    public void MethodsWithNullableReturnTypes_AreRegistered_Once(string returnType, string expectedReturnTypeInDeclaration)
    {
        var result = RunCecilifier(
            $$"""
              interface IFoo {} 
              class Foo : IFoo 
              { 
                 {{returnType}}? M() => default({{returnType}});
                 void M2() => M();
              }
              """);
        
        var actual = result.GeneratedCode.ReadToEnd();
        Assert.That(
            Regex.Count(actual, $$"""var m_M_\d+ = new MethodDefinition\("M", MethodAttributes.+, {{expectedReturnTypeInDeclaration}}\);\n"""), 
            Is.EqualTo(1), 
            actual);
    }

    [TestCase(
        """
        class Foo
        {
           int Bar(int? i) => i.Value;
           int? Test(int i1) { return Bar(i1); } // i1 should be converted to Nullable<int> and Bar() return also.
        }
        """,
        
        """
        //return Bar\(i1\);
        (\s+il_test_\d+\.Emit\(OpCodes\.)Ldarg_0\);
        \1Ldarg_1\);
        (?<implicit_nullable_conversion>\1Newobj, .+ImportReference\(typeof\(System.Nullable<>\).MakeGenericType\(typeof\(System.Int32\)\).GetConstructors\(\).+;)
        \1Call, m_bar_1\);
        \k<implicit_nullable_conversion>
        \1Ret\);
        """,
        TestName = "Method parameter and return value"
        )]
    
    [TestCase(
        """
        class Foo
        {
           void Bar(int? p)
           {
              p = 41;
              
              int ?lp;
              lp = 42;
           }
        }
        """,
        
        """
        //p = 41;
        (\s+il_bar_\d+\.Emit\(OpCodes\.)Ldc_I4, 41\);
        \1Newobj, .+ImportReference\(typeof\(System.Nullable<>\).MakeGenericType\(typeof\(System.Int32\)\).GetConstructors\(\).+;
        \1Starg_S, p_p_3\);
        """,
        TestName = "Variable assignment"
        )]
    public void ImplicitNullableConversions_AreApplied(string code, string expectedSnippet)
    {
        //https://github.com/adrianoc/cecilifier/issues/251
        var result = RunCecilifier(code);
        Assert.That(result.GeneratedCode.ReadToEnd(),  Does.Match(expectedSnippet));
    }

    [Test]
    public void ConstructorIsInvokedAfterCast()
    {
        var result = RunCecilifier("""int? M(object o) => (int) o;""");
        Assert.That(result.GeneratedCode.ReadToEnd(),  Does.Match("""
                                                                  \s+//\(int\) o
                                                                  (?<emit>\s+il_M_\d+\.Emit\(OpCodes\.)Ldarg_0\);
                                                                  \k<emit>Unbox_Any, assembly.MainModule.TypeSystem.Int32\);
                                                                  \k<emit>Newobj,.+ImportReference\(typeof\(System.Nullable<>\)\.MakeGenericType\(typeof\(System.Int32\)\)\.GetConstructors\(\).+\);
                                                                  \k<emit>Ret\);
                                                                  """));
    }

    [TestCase(/*language=C#*/
        //"class C { void M(System.DateTime p) => p = default; }",
        "class C { void M(int? p) => p = null; }",
        """
        //p = null
        \s+il_M_2.Emit\(OpCodes.Ldarga, p_p_\d+\);
        \s+il_M_2.Emit\(OpCodes.Initobj, .+ImportReference\(typeof\(System.Nullable<>\)\).MakeGenericInstanceType\(.+Int32\)\);
        """,
        TestName = "Parameter")]
    [TestCase(/*language=C#*/
        "class C { void M()  { int? i;  i = null; } }",
        """
        //i = null;
        \s+il_M_2.Emit\(OpCodes.Ldloca, l_i_\d+\);
        \s+il_M_2.Emit\(OpCodes.Initobj, .+ImportReference\(typeof\(System.Nullable<>\)\).MakeGenericInstanceType\(.+Int32\)\);
        """,
        TestName = "Local variable assignment")]
    [TestCase(/*language=C#*/
        "class C { void M()  { int? i = null; } }",
        """
        //int\? i = null;
        \s+var (?<var>l_i_\d+) = new VariableDefinition\((?<nullable>.+ImportReference\(typeof\(System.Nullable<>\)\)\.MakeGenericInstanceType\(.+Int32\))\);
        \s+m_M_1.Body.Variables.Add\(\k<var>\);
        (?<emit>\s+il_M_\d+\.Emit\(OpCodes\.)Ldloca, \k<var>\);
        \k<emit>Initobj, \k<nullable>\);
        """,
        TestName = "Local variable initializer")]
    [TestCase(/*language=C#*/
        "class C { void M(int? p) => M(null); }", 
        """
        //M\(null\)
        (\s+il_M_\d+\.Emit\(OpCodes\.)Ldarg_0\);
        \s+var (?<var>l_tmpNull_\d+) = new VariableDefinition\(.+ImportReference\(.+System.Nullable<>\)\).MakeGenericInstanceType\(.+Int32\)\);
        \s+m_M_\d+.Body.Variables.Add\(\k<var>\);
        \1Ldloca_S, \k<var>\);
        \1Initobj, .+ImportReference\(typeof\(System.Nullable<>\)\).MakeGenericInstanceType\(.+Int32\)\);
        (\s+il_M_\d+\.Emit\(OpCodes\.)Ldloc_S, \k<var>\);
        \1Call, m_M_\d+\);
        """,
        TestName = "Argument")]
    [TestCase(/*language=C#*/
        "class C { void M(int? p) => f = null;  int ?f; }", 
        """
                      //f = null
                      (\s+il_M_\d+\.Emit\(OpCodes\.)Ldarg_0\);
                      \1Ldflda, fld_f_\d+\);
                      \1Initobj, .+ImportReference\(typeof\(System.Nullable<>\)\).MakeGenericInstanceType\(.+Int32\)\);
                      """,
        TestName = "Field")]
    public void InitializingWithNull_EmitsCorrectCode(string code, string expectedSnippet)
    {
        var result = RunCecilifier(code);
        Assert.That(result.GeneratedCode.ReadToEnd(),  Does.Match(expectedSnippet));
    }
}
