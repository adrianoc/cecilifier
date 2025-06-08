using Cecilifier.Core.Tests.Tests.Unit.Framework;
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

    [TestCase("string C<T>(T t) where T : struct => t.ToString();", """
                                                                    //Parameters of 'string C<T>\(T t\) where T : struct => t.ToString\(\);'
                                                                    \s+var (p_t_\d+) = new ParameterDefinition\("t", ParameterAttributes.None, (gp_T_\d+)\);
                                                                    \s+m_C_10.Parameters.Add\(\1\);
                                                                    \s+//t\.ToString\(\)
                                                                    (\s+il_C_\d+\.Emit\(OpCodes\.)Ldarga, \1\);
                                                                    \3Constrained, \2\);
                                                                    \3Callvirt, .+ImportReference\(.+ResolveMethod\(typeof\(System.Object\), "ToString",.+\)\)\);
                                                                    \3Ret\);
                                                                    """, TestName = "ParameterConstrainedToStruct")]
    
    [TestCase("string C<T>(T t) where T : IFoo => t.Get();",  """
                                                              //Parameters of 'string C<T>\(T t\) where T : IFoo => t.Get\(\);'
                                                              \s+var (p_t_\d+) = new ParameterDefinition\("t", ParameterAttributes.None, (gp_T_\d+)\);
                                                              \s+m_C_10.Parameters.Add\(\1\);
                                                              \s+//t\.Get\(\)
                                                              (\s+il_C_\d+\.Emit\(OpCodes\.)Ldarga, \1\);
                                                              \3Constrained, \2\);
                                                              \3Callvirt, m_get_1\);
                                                              \3Ret\);
                                                              """, TestName = "ParameterConstrainedToInterfaceCallInterfaceMethod")]
    
    [TestCase("string C<T>(T t) where T : IFoo => t.ToString();", """
                                                                  //Parameters of 'string C<T>\(T t\) where T : IFoo => t.ToString\(\);'
                                                                  \s+var (p_t_\d+) = new ParameterDefinition\("t", ParameterAttributes.None, (gp_T_\d+)\);
                                                                  \s+m_C_10.Parameters.Add\(\1\);
                                                                  \s+//t\.ToString\(\)
                                                                  (\s+il_C_\d+\.Emit\(OpCodes\.)Ldarga, \1\);
                                                                  \3Constrained, \2\);
                                                                  \3Callvirt, .+ImportReference\(.+ResolveMethod\(typeof\(System.Object\), "ToString",.+\)\)\);
                                                                  \3Ret\);
                                                                  """, TestName = "ParameterConstrainedToInterfaceCallToString")]
    
    [TestCase("string C<T>(T t) => t.ToString();", """
                                                   //Parameters of 'string C<T>\(T t\) => t.ToString\(\);'
                                                   \s+var (p_t_\d+) = new ParameterDefinition\("t", ParameterAttributes.None, (gp_T_\d+)\);
                                                   \s+m_C_10.Parameters.Add\(\1\);
                                                   \s+//t\.ToString\(\)
                                                   (\s+il_C_\d+\.Emit\(OpCodes\.)Ldarga, \1\);
                                                   \3Constrained, \2\);
                                                   \3Callvirt, .+ImportReference\(.+ResolveMethod\(typeof\(System.Object\), "ToString",.+\)\)\);
                                                   \3Ret\);
                                                   """, TestName = "ParameterUnconstrained")]
    
    [TestCase("string C<T>(T t) where T : class => t.ToString();", """
                                                                   //Parameters of 'string C<T>\(T t\) where T : class => t.ToString\(\);'
                                                                   \s+var (p_t_\d+) = new ParameterDefinition\("t", ParameterAttributes.None, (gp_T_\d+)\);
                                                                   \s+m_C_10.Parameters.Add\(\1\);
                                                                   \s+//t\.ToString\(\)
                                                                   (\s+il_C_\d+\.Emit\(OpCodes\.)Ldarg_1\);
                                                                   \3Box, \2\);
                                                                   \3Callvirt, .+ImportReference\(.+ResolveMethod\(typeof\(System.Object\), "ToString",.+\)\)\);
                                                                   \3Ret\);
                                                                   """, TestName = "ParameterConstrainedToReferenceType")]
    
    [TestCase("string C<T>(T t) where T : Foo => t.ToString();", """
                                                                 //Parameters of 'string C<T>\(T t\) where T : Foo => t.ToString\(\);'
                                                                 \s+var (p_t_\d+) = new ParameterDefinition\("t", ParameterAttributes.None, (gp_T_\d+)\);
                                                                 \s+m_C_10.Parameters.Add\(\1\);
                                                                 \s+//t.ToString\(\)
                                                                 (\s+il_C_\d+\.Emit\(OpCodes\.)Ldarg_1\);
                                                                 \3Box, \2\);
                                                                 \3Callvirt, .+ImportReference\(.+ResolveMethod\(typeof\(System.Object\), "ToString",.+\)\)\);
                                                                 \3Ret\);
                                                                 """, TestName = "ParameterConstrainedToClassCallToString")]

    [TestCase("void C<T>(T t) where T : Foo => t.M();", """
                                                        (il_C_\d+\.Emit\(OpCodes\.)Ldarg_1\);
                                                        \s+\1Box, gp_T_11\);
                                                        \s+\1Callvirt, m_M_5\);
                                                        \s+\1Ret\);
                                                        """, TestName = "ParameterConstrainedToClassCallClassMethod")]

    [TestCase("string C<T>(T t) where T : struct { var l = t; return l.ToString(); }", """
                                                                                       //return l.ToString\(\);
                                                                                       (\s+il_C_\d+\.Emit\(OpCodes\.)Ldloca, l_l_14\);
                                                                                       \1Constrained, gp_T_11\);
                                                                                       \1Callvirt, .+ImportReference\(.+ResolveMethod\(typeof\(System.Object\), "ToString",.+\)\)\);
                                                                                       \1Ret\);
                                                                                       """, TestName = "LocalConstrainedToStruct")]
    
    [TestCase("bool C<T>(T t) where T : struct { var l = t; return l.Equals(l); }", """
                                                                                       //return l.Equals\(l\);
                                                                                       (\s+il_C_\d+\.Emit\(OpCodes\.)Ldloca, l_l_14\);
                                                                                       \1Ldloc, l_l_14\);
                                                                                       \1Box, gp_T_11\);
                                                                                       \1Constrained, gp_T_11\);
                                                                                       \1Callvirt, .+ImportReference\(.+ResolveMethod\(typeof\(System.Object\), "Equals",.+\)\)\);
                                                                                       \1Ret\);
                                                                                       """, TestName = "LocalConstrainedToStructCallEquals")]

    [TestCase("string C<T>(T t) where T : IFoo { var l = t; return l.Get(); }", """
                                                                                //return l.Get\(\);
                                                                                (\s+il_C_\d+\.Emit\(OpCodes\.)Ldloca, l_l_14\);
                                                                                \1Constrained, gp_T_11\);
                                                                                \1Callvirt, m_get_1\);
                                                                                \1Ret\);
                                                                                """, TestName = "LocalConstrainedToInterfaceCallInterfaceMethod")]
    
    [TestCase("string C<T>(T t) where T : IFoo { var l = t; return l.ToString(); }", """
                                                                                     //return l.ToString\(\);
                                                                                     (\s+il_C_\d+\.Emit\(OpCodes\.)Ldloca, l_l_14\);
                                                                                     \1Constrained, gp_T_11\);
                                                                                     \1Callvirt, .+ImportReference\(.+ResolveMethod\(typeof\(System.Object\), "ToString",.+\)\)\);
                                                                                     \1Ret\);
                                                                                     """, TestName = "LocalConstrainedToInterfaceCallToString")]
    
    [TestCase("string C<T>(T t) { var l = t; return l.ToString(); }", """
                                                                      //return l.ToString\(\);
                                                                      (\s+il_C_\d+\.Emit\(OpCodes\.)Ldloca, l_l_14\);
                                                                      \1Constrained, gp_T_11\);
                                                                      \1Callvirt, .+ImportReference\(.+ResolveMethod\(typeof\(System.Object\), "ToString",.+\)\)\);
                                                                      \1Ret\);
                                                                      """, TestName = "LocalUnconstrained")]
    
    [TestCase("string C<T>(T t) where T : class { var l = t; return l.ToString(); }", """
                                                                                      //return l.ToString\(\);
                                                                                      (\s+il_C_\d+\.Emit\(OpCodes\.)Ldloc, l_l_14\);
                                                                                      \1Box, gp_T_11\);
                                                                                      \1Callvirt, .+ImportReference\(.+ResolveMethod\(typeof\(System.Object\), "ToString",.+\)\)\);
                                                                                      \1Ret\);
                                                                                      """, TestName = "LocalConstrainedToClassCallToString")]
    
    [TestCase("string C<T>(T t) where T : Foo { var l = t; return l.ToString(); }", """
                                                                                    //return l.ToString\(\);
                                                                                    (\s+il_C_\d+\.Emit\(OpCodes\.)Ldloc, l_l_14\);
                                                                                    \1Box, gp_T_11\);
                                                                                    \1Callvirt, .+ImportReference\(.+ResolveMethod\(typeof\(System.Object\), "ToString",.+\)\)\);
                                                                                    \1Ret\);
                                                                                    """, TestName = "LocalConstrainedToClassTypeCallToString")]
    
    [TestCase("void C<T>(T t) where T : Foo { var l = t; l.M(); }", """
                                                                    //l.M\(\);
                                                                    (\s+il_C_\d+\.Emit\(OpCodes\.)Ldloc, l_l_\d+\);
                                                                    \1Box, gp_T_\d+\);
                                                                    \1Callvirt, m_M_\d+\);
                                                                    \1Ret\);
                                                                    """, TestName = "LocalConstrainedToTypeCallClassMethod")]

    [TestCase("int C<T>(T t) where T : IFoo => t.Property;", """
                                                             (il_C_\d+\.Emit\(OpCodes\.)Ldarga, p_t_13\);
                                                             \s+\1Constrained, gp_T_11\);
                                                             \s+\1Callvirt, m_get_3\);
                                                             \s+\1Ret\);
                                                             """, TestName = "ParameterConstrainedToInterfaceAccessProperty")] // m_get_3 should be the getter on the interface (as opposed to the one in the class)
    
    [TestCase("int C<T>(T t) where T : Foo => t.Property;", """
                                                            (il_C_\d+\.Emit\(OpCodes\.)Ldarg_1\);
                                                            \s+\1Box, gp_T_11\);
                                                            \s+\1Callvirt, m_get_8\);
                                                            \s+\1Ret\);
                                                            """, TestName = "ParameterConstrainedToClassProperty")]

    [TestCase("int C<T>(T t) where T : IFoo { var l = t; return l.Property; }", """
                                                                                //return l.Property;
                                                                                (\s+il_C_\d+\.Emit\(OpCodes\.)Ldloca, l_l_14\);
                                                                                \1Constrained, gp_T_11\);
                                                                                \1Callvirt, m_get_3\);
                                                                                \1Ret\);
                                                                                """ , TestName = "LocalConstrainedToInterfaceAccessProperty")]
    
    [TestCase("int C<T>(T t) where T : Foo  { var l = t; return l.Property; }", """
                                                                                //return l.Property;
                                                                                (\s+il_C_\d+\.Emit\(OpCodes\.)Ldloc, l_l_14\);
                                                                                \1Box, gp_T_11\);
                                                                                \1Callvirt, m_get_8\);
                                                                                \1Ret\);
                                                                                """, TestName = "LocalConstrainedToTypeAccessProperty")]
    public void TestCallOn_TypeParameter_CallOnParametersAndLocals(string snippet, string expectedExpressions)
    {
        var code = $@"interface IFoo {{ string Get(); int Property {{ get; }} }}
class Foo
{{
    void M() {{ }}

    int Property => 42; 
    
    {snippet}
}}
";
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match(expectedExpressions));
    }

    [TestCase("where T : struct", "string ConstrainedToStruct() => field.ToString();", "Ldflda", "Constrained, gp_T_3")]
    [TestCase("where T : IFoo", "string ConstrainedToInterfaceCallInterfaceMethod() => field.Get();", "Ldflda", "Constrained, gp_T_3")]
    [TestCase("where T : IFoo", "string ConstrainedToInterfaceCallToString() => field.ToString();", "Ldflda", "Constrained, gp_T_3")]
    [TestCase("", "string Unconstrained() => field.ToString();", "Ldflda", "Constrained, gp_T_3")]
    [TestCase("where T : class", "string ConstrainedToReferenceType() => field.ToString();", """Ldfld""", "Box, gp_T_3")]
    [TestCase("where T: Bar", "string ConstrainedToClassCallToString() => field.ToString();", "Ldfld", "Box, gp_T_8")]
    [TestCase("where T: Bar", "void ConstrainedToClassCallClassMethod() => field.M();", "Ldfld", "Box, gp_T_8")]
    public void TestCallOn_TypeParameter_CallOnField(string constraint, string snippet, string expectedLoadOpCode, string expectedConversionExpression)
    {
        var code = $@"interface IFoo {{ string Get(); }}
class Foo<T> {constraint}
{{
    {snippet}
    T field;
}}

class Bar {{ public void M() {{ }} }}
";
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match($"""{expectedLoadOpCode}, new FieldReference\(fld_field_\d+.Name, fld_field_\d+.FieldType, cls_foo_\d+.MakeGenericInstanceType\(gp_T_\d+\)\)"""));
        Assert.That(cecilifiedCode, Does.Match(expectedLoadOpCode));
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

    [TestCase("""
              class Base { public virtual void M() { } }
              class Derived : Base { public override void M()  { base.M(); } }
              """, TestName = "Same compilation Unit (Method)")]
    
    [TestCase("""
              class Base { public virtual int P { get; set; } }
              class Derived : Base { 
                  public override int P 
                  {
                    get { return base.P; }
                    set { base.P = value; }
                  }
              }
              """, TestName = "Same compilation Unit (Property)")]
    
    [TestCase("""
              class Base { public virtual event System.EventHandler MyEvent; }
              class Derived : Base 
              {
                  public override event System.EventHandler MyEvent
                  {
                      add { base.MyEvent += value; }
                      remove { base.MyEvent -= value; }
                  } 
              }
              """, true, TestName = "Same compilation Unit (Event)")]
    
    [TestCase("""
              using System;
              class Derived : Exception { public override Exception GetBaseException() => base.GetBaseException(); }
              """, TestName = "External type")]
    public void AccessingMember_ThroughBaseKeyword(string source, bool requiresExtraParameterInCall = false)
    {
        var result = RunCecilifier(source);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var pattern = $"""
                      \s+//.*base\..+(?:\(\))?;?
                      (\s+il_.+\d+\.Emit\(OpCodes\.)Ldarg_0\);
                      { (requiresExtraParameterInCall ? @"(\s+il_.+\d+\.Emit\(OpCodes\.)Ldarg_1\);" : "")}
                      \1Call,.+\);
                      """;
        if (!requiresExtraParameterInCall)
            pattern = pattern.Replace("\n\n", "\n");
        
        Assert.That(cecilifiedCode, Does.Match(pattern));
    }

    [Test]
    public void GenericMemberReferences_Issue334()
    {
        var result = RunCecilifier("""
                                   using System; 
                                   class C<T> 
                                   { 
                                        T field; 
                                        event Action Event; 
                                        T Property {get; set; } 
                                        
                                        public void Set(T toSet) => field = toSet; 
                                        // Issue #339
                                        // public void SetEvent(Action toSet) => Event += toSet; 
                                        public void SetProperty(T toSet) => Property = toSet;
                                   }
                                   """);
        var actual = result.GeneratedCode.ReadToEnd();
        Assert.That(actual, Does.Match("""
                                          //field = toSet
                                          (?<emit>\s+il_set_\d+\.Emit\(OpCodes\.)Ldarg_0\);
                                          \k<emit>Ldarg_1\);
                                          \k<emit>Stfld, new FieldReference\(fld_field_\d+.Name, fld_field_\d+.FieldType, cls_C_\d+.MakeGenericInstanceType\(gp_T_\d+\)\)\);
                                          """));
        
        Assert.That(actual, Does.Match("""
                                          //Property = toSet
                                          (?<emit>\s+il_setProperty_\d+\.Emit\(OpCodes\.)Ldarg_0\);
                                          \k<emit>Ldarg_1\);
                                          \s+var (?<setter>r_set_Property_\d+) = new MethodReference\(l_set_\d+.Name, l_set_\d+.ReturnType\) {.+DeclaringType = cls_C_\d+.MakeGenericInstanceType\(gp_T_\d+\).+};
                                          \s+foreach\(var p in l_set_\d+.Parameters\)
                                          \s+{
                                          \s+\k<setter>\.Parameters.Add\(new ParameterDefinition\(p.Name, p.Attributes, p.ParameterType\)\);
                                          \s+}
                                          \k<emit>Call, \k<setter>\);
                                          """));
        // Placeholder for events. Ignore(#339)
        // Assert.That(actual, Does.Match("""
        //                                   //Event += toSet
        //                                   ....
        //                                   """));
    }
    
    [Test]
    public void GenericRefMemberReferences_Issue340()
    {
        var result = RunCecilifier("""
                                   using System;
                                   using System.Runtime.CompilerServices;
                                   
                                   var f = new Ref<int>();
                                   f.Print();
                                   
                                   ref struct Ref<T>
                                   {
                                       private ref T item;
                                       public void Print() => Console.WriteLine(item);
                                   }
                                   """);
        var actual = result.GeneratedCode.ReadToEnd();
        Assert.That(actual, Does.Match("""
                                          //Console.WriteLine\(item\)
                                          (?<emit>\s+il_print_\d+.Emit\(OpCodes\.)Ldarg_0\);
                                          \k<emit>Ldfld, new FieldReference\((?<fieldRef>fld_item_\d+).Name, \k<fieldRef>.FieldType, st_ref_0.MakeGenericInstanceType\(gp_T_1\).+\);
                                          \k<emit>Ldobj, gp_T_1\);
                                          \k<emit>Box, gp_T_1\);
                                          \k<emit>Call, .+"WriteLine".+\);
                                          \k<emit>Ret\);
                                          """));
        
    }
}
