using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class MemberAccessTests : CecilifierUnitTestBase
{
    [TestCase("p", TestName = "Parameter")]
    [TestCase("lr", TestName = "Local")]
    public void TestRefTarget_Class(string target)
    {
        var result = RunCecilifier($@"class Foo {{ int value; void Bar(ref Foo p)  {{ ref Foo lr = ref p; {target}.value = 42; }} }}");
        Assert.That(
            result.GeneratedCode.ReadToEnd(), 
            Does.Match(
                @"(?<il>il_bar_\d+\.Emit\(OpCodes.)(?:Ldarg_1|Ldloc,.+)\);\s+" +
			        @"\k<il>Ldind_Ref\);\s+" +
                    @"\k<il>Ldc_I4, 42\);\s+" +
		            @"\k<il>Stfld, fld_value_1\);"));
    }
    
    [TestCase("p", TestName = "Parameter")]
    [TestCase("lr", TestName = "Local")]
    public void TestRefTarget_Struct(string target)
    {
        var result = RunCecilifier($@"struct Foo {{ int value; void Bar(ref Foo p)  {{ ref Foo lr = ref p; {target}.value = 42; }} }}");
        Assert.That(
            result.GeneratedCode.ReadToEnd(), 
            Does.Match(
                @"(?<il>il_bar_\d+\.Emit\(OpCodes.)(?:Ldarg_1|Ldloc,.+)\);\s+" +
                    @"\k<il>Ldc_I4, 42\);\s+" +
		            @"\k<il>Stfld, fld_value_1\);"));
    }

    [TestCase("string C<T>(T t) where T : struct => t.ToString();", "Ldarga, p_t_8", "Constrained, gp_T_6", TestName = "ParameterConstrainedToStruct")]
    [TestCase("string C<T>(T t) where T : IFoo => t.Get();", "Ldarga, p_t_8", "Constrained, gp_T_6", TestName = "ParameterConstrainedToInterfaceCallInterfaceMethod")]
    [TestCase("string C<T>(T t) where T : IFoo => t.ToString();", "Ldarga, p_t_8", "Constrained, gp_T_6", TestName = "ParameterConstrainedToInterfaceCallToString")]
    [TestCase("string C<T>(T t) => t.ToString();", "Ldarga, p_t_8", "Constrained, gp_T_6", TestName = "ParameterUnconstrained")]
    [TestCase("string C<T>(T t) where T : class => t.ToString();", "Ldarg_1", "Box, gp_T_6", TestName = "ParameterConstrainedToReferenceType")]
    [TestCase("string C<T>(T t) where T : Foo => t.ToString();", "Ldarg_1", "Box, gp_T_6", TestName = "ParameterConstrainedToClassCallToString")]
    [TestCase("void C<T>(T t) where T : Foo => t.M();", "Ldarg_1", "Box, gp_T_6", TestName = "ParameterConstrainedToClassCallClassMethod")]
    
    [TestCase("string C<T>(T t) where T : struct { var l = t; return l.ToString(); }", "Ldloca, l_l_9", "Constrained, gp_T_6", TestName = "LocalConstrainedToStruct")]
    [TestCase("string C<T>(T t) where T : IFoo { var l = t; return l.Get(); }", "Ldloca, l_l_9", "Constrained, gp_T_6", TestName = "LocalConstrainedToInterfaceCallInterfaceMethod")]
    [TestCase("string C<T>(T t) where T : IFoo { var l = t; return l.ToString(); }", "Ldloca, l_l_9", "Constrained, gp_T_6", TestName = "LocalConstrainedToInterfaceCallToString")]
    [TestCase("string C<T>(T t) { var l = t; return l.ToString(); }", "Ldloca, l_l_9", "Constrained, gp_T_6", TestName = "LocalUnconstrained")]
    [TestCase("string C<T>(T t) where T : class { var l = t; return l.ToString(); }", "Ldloc, l_l_9", "Box, gp_T_6", TestName = "LocalConstrainedToReferenceType")]
    [TestCase("string C<T>(T t) where T : Foo { var l = t; return l.ToString(); }", "Ldloc, l_l_9", "Box, gp_T_6", TestName = "LocalConstrainedToClassCallToString")]
    [TestCase("void C<T>(T t) where T : Foo { var l = t; l.M(); }", "Ldloc, l_l_9", "Box, gp_T_6", TestName = "LocalConstrainedToClassCallClassMethod")]

    [TestCase("int C<T>(T t) where T : IFoo => t.Property;", "Ldarga, p_t_8", "Constrained, gp_T_6", TestName = "ParameterConstrainedToInterfaceProperty")]
    [TestCase("int C<T>(T t) where T : Foo => t.Property;", "Ldarg_1", "Box, gp_T_6", TestName = "ParameterConstrainedToClassProperty")]

    [TestCase("int C<T>(T t) where T : IFoo { var l = t; return l.Property; }", "Ldloca, l_l_9", "Constrained, gp_T_6", TestName = "LocalConstrainedToInterfaceProperty")]
    [TestCase("int C<T>(T t) where T : Foo  { var l = t; return l.Property; }", "Ldloc, l_l_9", "Box, gp_T_6", TestName = "LocalConstrainedToClassProperty")]
    public void TestCallOn_TypeParameter_CallOnParametersAndLocals(string snippet, params string[] expectedExpressions)
    {
        var code =$@"interface IFoo {{ string Get(); int Property {{ get; }} }}
class Foo
{{
    {snippet}

    void M() {{ }}

    int Property => 42; 
}}
";
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        foreach(var expectedExpression in expectedExpressions)
            Assert.That(cecilifiedCode, Contains.Substring(expectedExpression));
    }
    
    [TestCase("where T : struct", "string ConstrainedToStruct() => field.ToString();",  "Ldflda, fld_field_6", "Constrained, gp_T_3")]
    [TestCase("where T : IFoo", "string ConstrainedToInterfaceCallInterfaceMethod() => field.Get();", "Ldflda, fld_field_6", "Constrained, gp_T_3")]
    [TestCase("where T : IFoo", "string ConstrainedToInterfaceCallToString() => field.ToString();", "Ldflda, fld_field_6", "Constrained, gp_T_3")]
    [TestCase("", "string Unconstrained() => field.ToString();", "Ldflda, fld_field_6", "Constrained, gp_T_3")]
    [TestCase("where T : class", "string ConstrainedToReferenceType() => field.ToString();", "Ldfld, fld_field_6", "Box, gp_T_3")]
    [TestCase("where T: Bar", "string ConstrainedToClassCallToString() => field.ToString();", "Ldfld, fld_field_11", "Box, gp_T_8")]
    [TestCase("where T: Bar", "void ConstrainedToClassCallClassMethod() => field.M();", "Ldfld, fld_field_11", "Box, gp_T_8")]
    public void TestCallOn_TypeParameter_CallOnField(string constraint, string snippet, params string[] expectedExpressions)
    {
        var code =$@"interface IFoo {{ string Get(); }}
class Foo<T> {constraint}
{{
    {snippet}
    T field;
}}

class Bar {{ public void M() {{ }} }}
";
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        foreach(var expectedExpression in expectedExpressions)
            Assert.That(cecilifiedCode, Contains.Substring(expectedExpression));
    }

    // Issue #187
    [TestCase("class Foo { static int X { get; } static int M() => X; }", "m_get_2", TestName = "Property")]
    [TestCase("class Foo { static event System.Action E; static void M() => E += M; }", "m_add_2", TestName = "Event")]
    public void StaticMembers(string source, string expectedMethodCall)
    {
        var result = RunCecilifier(source);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Not.Match(
            @"(il_M_\d+\.Emit\(OpCodes\.)Ldarg_0\);\s+" +
			$@"\1Call, {expectedMethodCall}\);\s+"), 
            "ldarg.0 is not expected");
    }
}
