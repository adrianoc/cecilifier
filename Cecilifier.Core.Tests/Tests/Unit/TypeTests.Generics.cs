using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class GenericTypeTests : CecilifierUnitTestBase
{
    [TestCase("var x = typeof(System.Collections.Generic.Dictionary<int, int>.Enumerator);",
        """il_topLevelMain_3.Emit\(OpCodes.Ldtoken, assembly.MainModule.ImportReference\(typeof\(System.Collections.Generic.Dictionary<int, int>.Enumerator\)\)\);""")]

    [TestCase("class Foo { void Bar(System.Collections.Generic.Dictionary<int, int> dict) { var enu = dict.GetEnumerator(); } }",
        """
        (il_bar_\d+).Emit\(OpCodes.Callvirt,.+ImportReference\(.+typeof\(System.Collections.Generic.Dictionary<System.Int32, System.Int32>\), "GetEnumerator",.+\)\);
        \s+\1.Emit\(OpCodes.Stloc, l_enu_\d+\);
        """)]
    public void TestReferenceToNonGenericInnerTypeOfGenericOuterType(string code, string expected)
    {
        var result = RunCecilifier(code);
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expected));
    }
    

    [Test]
    public void TestInnerTypeOfOuterGenericType()
    {
        var r = RunCecilifier("class C { System.Collections.Generic.List<int>.Enumerator e; }");
        Assert.That(
            r.GeneratedCode.ReadToEnd(),
            Does.Match("""var fld_e_1 = new FieldDefinition\("e", FieldAttributes.Private, assembly.MainModule.ImportReference\(typeof\(System.Collections.Generic.List<int>.Enumerator\)\)\);"""));
    }
    
    [Test]
    public void TestRecursiveTypeParameterConstraint()
    {
        //Test for issue #218
        var r = RunCecilifier("interface I<T> where T : I<T> {}");
        Assert.That(
            r.GeneratedCode.ReadToEnd(),
            Does.Match(
                """
                var gp_T_1 = new Mono.Cecil.GenericParameter\("T", itf_I_0\);
                \s+itf_I_0.GenericParameters.Add\(gp_T_1\);
                \s+gp_T_1.Constraints.Add\(new GenericParameterConstraint\(itf_I_0.MakeGenericInstanceType\(gp_T_1\)\)\);
                """));
    }

    [Test]
    public void TypeInheritingFromGenericTypeTakingItAsTypeArgument()
    {
        //Test for issue #297
        var result = RunCecilifier("""
                                   class Foo<T> : System.IEquatable<Foo<T>>
                                   {
                                       public bool Equals(Foo<T> other) => false;
                                   }
                                   """);
        
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(
            cecilifiedCode, 
            Does.Match(@"(cls_foo_\d+).Interfaces.Add\(.+ImportReference\(typeof\(System.IEquatable<>\)\).MakeGenericInstanceType\(\1.MakeGenericInstanceType\(gp_T_\d+\)\)\)\);"));
    }
}
