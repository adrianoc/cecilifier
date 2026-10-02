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
        \s+var m_declaringType_7 = assembly.MainModule.ImportReference\(typeof\(System.Nullable<>\)\).MakeGenericInstanceType\(assembly.MainModule.TypeSystem.Int32\);
        \s+var r_tmpMethod_8 = assembly.MainModule.ImportReference\(m_declaringType_7.ElementType.Resolve\(\).Methods.Single\(m => m.Name == ".ctor" && m.Parameters.Count == 1\)\);
        \s+var r_genericMethod_9 = new MethodReference\(r_tmpMethod_8.Name, r_tmpMethod_8.ReturnType\)
        \s+{
        \s+HasThis = r_tmpMethod_8.HasThis,
        \s+CallingConvention = r_tmpMethod_8.CallingConvention,
        \s+ExplicitThis = r_tmpMethod_8.ExplicitThis,
        \s+DeclaringType = m_declaringType_7,
        \s+};
        \s+r_genericMethod_9.Parameters.Add\(new ParameterDefinition\(r_tmpMethod_8.Parameters\[0\].Name, r_tmpMethod_8.Parameters\[0\].Attributes, r_tmpMethod_8.Parameters\[0\].ParameterType\)\);
        \s+il_test_5.Emit\(OpCodes.Newobj, r_genericMethod_9\);
        \s+il_test_5.Emit\(OpCodes.Call, m_bar_1\);
        \s+var m_declaringType_10 = assembly.MainModule.ImportReference\(typeof\(System.Nullable<>\)\).MakeGenericInstanceType\(assembly.MainModule.TypeSystem.Int32\);
        \s+var r_tmpMethod_11 = assembly.MainModule.ImportReference\(m_declaringType_10.ElementType.Resolve\(\).Methods.Single\(m => m.Name == ".ctor" && m.Parameters.Count == 1\)\);
        \s+var r_genericMethod_12 = new MethodReference\(r_tmpMethod_11.Name, r_tmpMethod_11.ReturnType\)
        \s+{
        \s+HasThis = r_tmpMethod_11.HasThis,
        \s+CallingConvention = r_tmpMethod_11.CallingConvention,
        \s+ExplicitThis = r_tmpMethod_11.ExplicitThis,
        \s+DeclaringType = m_declaringType_10,
        \s+};
        \s+r_genericMethod_12.Parameters.Add\(new ParameterDefinition\(r_tmpMethod_11.Parameters\[0\].Name, r_tmpMethod_11.Parameters\[0\].Attributes, r_tmpMethod_11.Parameters\[0\].ParameterType\)\);
        \1Newobj, r_genericMethod_12\);
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
        \s+var m_declaringType_4 = .+ImportReference\(typeof\(System.Nullable<>\)\).MakeGenericInstanceType\(assembly.MainModule.TypeSystem.Int32\);
        \s+var r_tmpMethod_5 = .+ImportReference\(m_declaringType_4.ElementType.Resolve\(\).Methods.Single\(m => m.Name == ".ctor" && m.Parameters.Count == 1\)\);
        \s+var r_genericMethod_6 = new MethodReference\(r_tmpMethod_5.Name, r_tmpMethod_5.ReturnType\)
        \s+{
        \s+HasThis = r_tmpMethod_5.HasThis,
        \s+CallingConvention = r_tmpMethod_5.CallingConvention,
        \s+ExplicitThis = r_tmpMethod_5.ExplicitThis,
        \s+DeclaringType = m_declaringType_4,
        \s+};
        \s+r_genericMethod_6.Parameters.Add\(new ParameterDefinition\(.+Parameters\[0\].Name, .+Parameters\[0\].Attributes, .+.Parameters\[0\].ParameterType\)\);
        \1Newobj, r_genericMethod_6\);
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
                                                                  \s+var il_M_\d+ = m_M_\d+.Body.GetILProcessor\(\);
                                                                  (?<emit>\s+il_M_\d+\.Emit\(OpCodes\.)Ldarg_0\);
                                                                  \k<emit>Unbox_Any, assembly.MainModule.TypeSystem.Int32\);
                                                                  \s+var m_declaringType_9 = assembly.MainModule.ImportReference\(typeof\(System.Nullable<>\)\).MakeGenericInstanceType\(.+Int32\);
                                                                  \s+var r_tmpMethod_10 = assembly.MainModule.ImportReference\(m_declaringType_9.ElementType.Resolve\(\).Methods.Single\(m => m.Name == ".ctor" && m.Parameters.Count == 1\)\);
                                                                  \s+var r_genericMethod_11 = new MethodReference\(r_tmpMethod_10.Name, r_tmpMethod_10.ReturnType\)
                                                                  \s+{
                                                                  \s+HasThis = r_tmpMethod_10.HasThis,
                                                                  \s+CallingConvention = r_tmpMethod_10.CallingConvention,
                                                                  \s+ExplicitThis = r_tmpMethod_10.ExplicitThis,
                                                                  \s+DeclaringType = m_declaringType_9,
                                                                  \s+};
                                                                  \s+r_genericMethod_11.Parameters.Add\(new ParameterDefinition\(.+Parameters\[0\].Name, .+Parameters\[0\].Attributes, .+Parameters\[0\].ParameterType\)\);
                                                                  \k<emit>Newobj, r_genericMethod_11\);
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
