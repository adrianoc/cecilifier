using System.Text.RegularExpressions;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class PropertyTests : CecilifierUnitTestBase
{
    [Test]
    public void TestGetterOnlyInitialization_Simple()
    {
        var result = RunCecilifier("class C { public int Value { get; } public C() => Value = 42; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Contains.Substring(
            @"il_ctor_8.Emit(OpCodes.Ldarg_0);
			il_ctor_8.Emit(OpCodes.Ldc_I4, 42);
			il_ctor_8.Emit(OpCodes.Stfld, fld_value_4);"));
    }

    [Test]
    public void TestGetterOnlyInitialization_Complex()
    {
        var result = RunCecilifier("class C { public int Value { get; } public C(int n) => Value = n * 2; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Contains.Substring(
            @"il_ctor_8.Emit(OpCodes.Ldarg_0);
			il_ctor_8.Emit(OpCodes.Ldarg_1);
			il_ctor_8.Emit(OpCodes.Ldc_I4, 2);
			il_ctor_8.Emit(OpCodes.Mul);
			il_ctor_8.Emit(OpCodes.Stfld, fld_value_4);"));
    }

    [Test]
    public void TestAutoPropertyWithGetterAndSetter()
    {
        var result = RunCecilifier("class C { public int Value { get; set; } public C() => Value = 42; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match(
            """
            (\s+il_ctor_\d+.Emit\(OpCodes\.)Ldc_I4, 42\);
            \1Call, l_set_\d+\);
            """));
    }

    [Test]
    public void TestPropertyInitializers()
    {
        var result = RunCecilifier("class C { int Value1 { get; } = 42;  int Value2 { get; } = M(21); static int M(int v) => v * 2; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match(
            @"//int Value1 { get; } = 42;\s+" +
            @"(il_ctor_C_17\.Emit\(OpCodes\.)Ldarg_0\);\s+" +
            @"\1Ldc_I4, 42\);\s+" +
            @"\1Stfld, fld_value1_4\);\s+" +
            @"//int Value2 { get; } = M\(21\);\s+" +
            @"(il_ctor_C_17\.Emit\(OpCodes\.)Ldarg_0\);\s+" +
            @"\1Ldc_I4, 21\);\s+" +
            @"\1Call, m_M_7\);\s+" +
            @"\1Stfld, fld_value2_13\);"));
    }

    [Test]
    public void TestSystemIndexPropertyInitializers()
    {
        var result = RunCecilifier("using System; class C { Index Value1 { get; } = ^42; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match(
            @"//Index Value1 { get; } = \^42;\s+" +
            @"il_ctor_C_8.Emit\(OpCodes.Ldarg_0\);\s+" +
            @"il_ctor_C_8.Emit\(OpCodes.Ldc_I4, 42\);\s+" +
            @"il_ctor_C_8.Emit\(OpCodes.Ldc_I4_1\);\s+" +
            @"il_ctor_C_8.Emit\(OpCodes.Newobj,.+typeof\(System.Index\), ""\.ctor"",.+""System.Int32"", ""System.Boolean"".+\);\s+" +
            @"il_ctor_C_8.Emit\(OpCodes.Stfld, fld_value1_4\);"));
    }

    [TestCase("get { int l = 42; return l; }", "m_get_2", TestName = "Getter")]
    [TestCase("set { int l = value; }", "l_set_2", TestName = "Setter")]
    [TestCase("init { int l = value; }", "l_set_2", TestName = "Init")]
    public void TestPropertyAccessorWithLocalVariables(string accessorDeclaration, string targetMethod)
    {
        var result = RunCecilifier($"class C {{ int Value {{ {accessorDeclaration} }} }}");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match(
            $"""
             var (l_l_\d+) = new VariableDefinition\(assembly\.MainModule\.TypeSystem\.Int32\);
             \s+{targetMethod}\.Body\.Variables\.Add\(\1\);
             """));
    }

    [Test]
    public void Covariant()
    {
        var result = RunCecilifier("class B { public virtual B Prop => null; } class D : B { public override D Prop => new D(); D CallIt() => Prop; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Contains.Substring("var m_get_8 = new MethodDefinition(\"get_Prop\", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.NewSlot, cls_D_6);"));
        Assert.That(cecilifiedCode, Does.Match(@"m_get_8\.CustomAttributes\.Add\(.+typeof\(.+PreserveBaseOverridesAttribute\).+\);"));
        Assert.That(cecilifiedCode, Contains.Substring("il_callIt_12.Emit(OpCodes.Callvirt, m_get_8);"));
    }

    [Test]
    public void StaticAutomaticProperties()
    {
        var result = RunCecilifier("class C { public static int Prop { get; set; } } class Driver { int M() => C.Prop; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.Multiple(delegate 
        {
            Assert.That(cecilifiedCode, Does.Match("""
                                                   var fld_prop_\d+ = new FieldDefinition\("<Prop>k__BackingField",.+FieldAttributes\.Static \| FieldAttributes\.Private.+\);
                                                   \s+cls_C_\d+\.Fields\.Add\(fld_prop_\d+\);
                                                   """), "Backing field should be static");

            var matches = Regex.Matches(cecilifiedCode, 
                                                """
                                                   var fld_prop_\d+ = new FieldDefinition\(.+\);
                                                   \s+cls_C_\d+\.Fields\.Add\(fld_prop_\d+\);
                                                   """);
            
            Assert.That(matches.Count, Is.EqualTo(1), $"Only one backing field is expected:\n{cecilifiedCode}");
            
            Assert.That(cecilifiedCode, Does.Match(@"var l_set_\d+ = new MethodDefinition\(""set_Prop"", .+MethodAttributes\.Static.+\);"), "Setter should be static");
            Assert.That(cecilifiedCode, Does.Match(@"var m_get_\d+ = new MethodDefinition\(""get_Prop"", .+MethodAttributes\.Static.+\);"), "Getter should be static");
            Assert.That(cecilifiedCode, Does.Match(@"il_set_\d+.Emit\(OpCodes.Stsfld, fld_prop_\d+\);"), "Setter field access should be static");
            Assert.That(cecilifiedCode, Does.Match(@"il_get_\d+.Emit\(OpCodes.Ldsfld, fld_prop_\d+\);"), "Getter field access should be static");
        });
    }
    
    [Test]
    public void AccessorAccessibility_IsRespected()
    {
        var code = """
                   class Foo
                   {
                       public int Value
                       {
                           private get => 1;
                           set {  }
                       }
                   }
                   """;

        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilifiedCode, Does.Match("""
                                               //Getter
                                               \s+var m_get_\d+ = new MethodDefinition\("get_Value", MethodAttributes.Private.+, .+TypeSystem.Int32\);
                                               """));
        
        Assert.That(cecilifiedCode, Does.Match("""
                                               // Setter
                                               \s+var l_set_\d+ = new MethodDefinition\("set_Value", MethodAttributes.Public.+, .+TypeSystem.Void\);
                                               """));
    }
    
    [Test]
    public void AttributeTargetingFieldOfNonAutomaticProperties_DoesNot_EmitAttribute()
    {
        var code = """
                   using System;
                   class Foo
                   {
                       [field:Obsolete] // This is invalid; C# compiler emits a warning.
                       public int Value
                       {
                           private get => _v;
                           set => _v = 1;
                       }
                   
                       private int _v; // Not important
                   }
                   """;

        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Not.Match("""\s+\.CustomAttributes.Add\(attr_obsolete_\d+\);"""));
    }
    
    [Test]
    public void IndexersWithDefaultParameters_InitializesParameterValues()
    {
        var code = """
                   class Foo { public int this[int index, int value = 1, float floatWithUnaryOperator = -4.2f] => index + value; }
                   """;

        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match("""
                                               var (p_value_\d+) = new ParameterDefinition\("value", ParameterAttributes.Optional, assembly.MainModule.TypeSystem.Int32\);
                                               \s+\1\.Constant = 1;
                                               """));
        
        Assert.That(cecilifiedCode, Does.Match("""
                                               var (p_floatWithUnaryOperator_\d+) = new ParameterDefinition\("floatWithUnaryOperator", ParameterAttributes.Optional, assembly.MainModule.TypeSystem.Single\);
                                               \s+\1\.Constant = -4.2f;
                                               """));
    }
}
