using System.Text.RegularExpressions;
using NUnit.Framework;
using NUnit.Framework.Internal;

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

                (\s+il_test_\d+\.Emit\(OpCodes\.)Ldarg_0\);
                \1Ldarg_1\);
                \1NEWOBJ Nullable<int>
                \1Call, m_bar_1\);
                \1NEWOBJ Nullable<int>
                \1Ret\);
                """),
            "");
    }
}
