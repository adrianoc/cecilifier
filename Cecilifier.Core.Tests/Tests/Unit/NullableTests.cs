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

    [Test]
    public void ConstructorIsInvokedAfterCast()
    {
        var result = RunCecilifier("""int? M(object o) => (int) o;""");
        Assert.That(result.GeneratedCode.ReadToEnd(),  Does.Match("""
                                                                  \s+//\(int\) o
                                                                  \s+var il_M_\d+ = m_M_\d+.Body.GetILProcessor\(\);
                                                                  (?<emit>\s+il_M_\d+\.Emit\(OpCodes\.)Ldarg_0\);
                                                                  \k<emit>Unbox_Any, assembly.MainModule.TypeSystem.Int32\);
                                                                  \s+var m_declaringType_10 = assembly.MainModule.ImportReference\(typeof\(System.Nullable<>\)\).MakeGenericInstanceType\(.+Int32\);
                                                                  \s+var r_tmpMethod_11 = .+ImportReference\(.+FindBestOverload\(.+\)\);
                                                                  \s+var r__ctor_12 = new MethodReference\(r_tmpMethod_11.Name, r_tmpMethod_11.ReturnType\)
                                                                  \s+{
                                                                  \s+HasThis = r_tmpMethod_11.HasThis,
                                                                  \s+CallingConvention = r_tmpMethod_11.CallingConvention,
                                                                  \s+ExplicitThis = r_tmpMethod_11.ExplicitThis,
                                                                  \s+DeclaringType = m_declaringType_10,
                                                                  \s+};
                                                                  \s+r__ctor_12.Parameters.Add\(new ParameterDefinition\(.+Parameters\[0\].Name, .+Parameters\[0\].Attributes, .+Parameters\[0\].ParameterType\)\);
                                                                  \k<emit>Newobj, r__ctor_12\);
                                                                  """));
    }

    [TestCase(/*language=C#*/
        //"class C { void M(System.DateTime p) => p = default; }",
        "class C { void M(int? p) => p = null; }",
        """
        //p = null
        \s+var il_M_\d+ = m_M_\d+.Body.GetILProcessor\(\);
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
        \s+var il_M_\d+ = m_M_\d+.Body.GetILProcessor\(\);
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
                      \s+var il_M_\d+ = m_M_\d+.Body.GetILProcessor\(\);
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
