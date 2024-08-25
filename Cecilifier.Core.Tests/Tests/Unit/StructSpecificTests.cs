using System.Collections.Generic;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class StructSpecificTests : CecilifierUnitTestBase
{
    [Test]
    public void ReadOnlyStructDeclaration()
    {
        var result = RunCecilifier("readonly struct RO { }");
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(@$"st_rO_\d+\.CustomAttributes\.Add\(new CustomAttribute\(.+typeof\(System.Runtime.CompilerServices.IsReadOnlyAttribute\), "".ctor"".+\)\);"));
    }

    [TestCase("using System.Runtime.InteropServices; [StructLayout(LayoutKind.Auto, Size = 4)] struct S {}", "AutoLayout", TestName = "AutoLayout")]
    [TestCase("using System.Runtime.InteropServices; [StructLayout(LayoutKind.Explicit, Size = 42)] struct S {}", "ExplicitLayout", TestName = "ExplicitLayout")]
    [TestCase("struct S {}", "SequentialLayout", TestName = "DefaultLayout")]
    public void StructLayoutAttributeIsAdded(string code, string expected)
    {
        var result = RunCecilifier(code);
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(@$"TypeAttributes\.{expected}"));
    }

    [Test]
    public void RefStructDeclaration()
    {
        var result = RunCecilifier("ref struct RS { }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match(@"st_rS_\d+\.CustomAttributes\.Add\(new CustomAttribute\(.+typeof\(System.Runtime.CompilerServices.IsByRefLikeAttribute\), "".ctor"".+\)\);"));
        Assert.That(cecilifiedCode, Does.Match(@"attr_obsolete_\d+\.ConstructorArguments\.Add\(new CustomAttributeArgument\(.+Boolean, true\)\);"));
        Assert.That(cecilifiedCode, Does.Match(@"st_rS_\d+\.CustomAttributes\.Add\(attr_obsolete_\d+\);"));
    }
    
    [Test]
    public void AssignMemberToLocalVariableBoxed(
        [ValueSource(nameof(AssignMemberToLocalVariableBoxedStorageTypeScenarios))] AssignMemberToLocalVariableBoxedStorageTypeScenario storageScenarios, 
        [Values("object", "System.IDisposable")] string memberType)
    {
        var result = RunCecilifier(
            $$"""
                struct Test : System.IDisposable { public void Dispose() {} }              
                class D
                {
                     Test field;
                     System.IDisposable M(Test parameter)
                     {
                         Test local = parameter;
                         {{memberType}} l;
                         l = {{storageScenarios.Member}};

                         return {{storageScenarios.Member}};
                     }
                }
                """);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match(storageScenarios.ExpectedRegex));
    }

    public struct AssignMemberToLocalVariableBoxedStorageTypeScenario
    {
        public string Member;
        public string ExpectedRegex;

        public override string ToString() => Member;
    }
    
    private static IEnumerable<AssignMemberToLocalVariableBoxedStorageTypeScenario> AssignMemberToLocalVariableBoxedStorageTypeScenarios()
    {
        yield return new AssignMemberToLocalVariableBoxedStorageTypeScenario
        {
            Member = "parameter", 
            ExpectedRegex = """
            //l = parameter;
            (.+il_M_\d+\.Emit\(OpCodes\.)Ldarg_1\);
            \1Box, (st_test_\d+)\);
            \1Stloc, l_l_\d+\);
            
            .+//return parameter;
            \1Ldarg_1\);
            \1Box, \2\);
            \1Ret\);
            """
        };

        yield return new AssignMemberToLocalVariableBoxedStorageTypeScenario
        {
            Member = "field", 
            ExpectedRegex = """
            //l = field;
            (.+il_M_\d+\.Emit\(OpCodes\.)Ldarg_0\);
            \1Ldfld, (fld_field_\d+)\);
            \1Box, (st_test_\d+)\);
            \1Stloc, l_l_\d+\);
            
            .+//return field;
            \1Ldarg_0\);
            \1Ldfld, \2\);
            \1Box, \3\);
            \1Ret\);
            """
        };

        yield return new AssignMemberToLocalVariableBoxedStorageTypeScenario
        {
            Member = "local",
            ExpectedRegex = """
            //l = local;
            (.+il_M_\d+\.Emit\(OpCodes\.)Ldloc, (l_local_\d+)\);
            \1Box, (st_test_\d+)\);
            \1Stloc, l_l_\d+\);
            
            .+//return local;
            \1Ldloc, \2\);
            \1Box, \3\);
            \1Ret\);
            """
        };
    }
    
    [TestCase("parameter", TestName = "Parameter")]
    [TestCase("field", TestName = "Field")]
    [TestCase("local", TestName = "Local")]
    public void AssignmentToInterfaceTypedMember(string member)
    {
        var result = RunCecilifier(
            $$"""
                struct Test : System.IDisposable { public void Dispose() {} }              
                class D
                {
                     System.IDisposable field;
                     void M(System.IDisposable parameter)
                     {
                         System.IDisposable local;
                         {{member}} = new Test();
                     }
                }
                """);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(
            cecilifiedCode, 
            Does.Match(
                """
                      var (l_vt_\d+) = new VariableDefinition\((st_test_\d+)\);
                      .+m_M_\d+\.Body\.Variables\.Add\(\1\);
                      (.+il_M_\d+\.Emit\(OpCodes\.)Ldloca_S, \1\);
                      \3Initobj, \2\);
                      \3Ldloc, \1\);
                      \3Box, \2\);
                      \3Stfld, fld_field_\d+|.Stloc, l_local_\d+|Starg_S, p_parameter_\d+\);
                      """));
    }

    [TestCase("=> new Test();", TestName = "Bodied")]
    [TestCase("{ return new Test(); }", TestName = "Return")]
    public void ReturnStructInstantiationAsReferenceType(string body)
    {
        var result = RunCecilifier(
            $$"""
                struct Test : System.IDisposable 
                { 
                     public void Dispose() {}
                     System.IDisposable M() {{body}} 
                }              
                """);
        
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match(
             """
             \s+//(?:return )?new Test\(\);?
             .+var (l_vt_\d+) = new VariableDefinition\((st_test_\d+)\);
             .+m_M_3.Body.Variables.Add\(\1\);
             (.+il_M_\d+.Emit\(OpCodes\.)Ldloca_S, \1\);
             \3Initobj, \2\);
             \3Ldloc, \1\);
             \3Box, \2\);
             \3Ret\);
             """));
    }

    [TestCase("1 + 2")]
    [TestCase("new Foo(1) + 2")]
    public void TestInvocationOnParenthesizedExpression(string expression)
    {
        var result = RunCecilifier(
            $$"""
              using System;
              Console.WriteLine( ({{expression}}).ToString() );

              struct Foo
              {
                  public Foo(int i) {}
                  public static implicit operator Foo(int i) => new Foo();
                  public static implicit operator int(Foo f) => 0;
              }
              """);
        
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilifiedCode, Does.Match("""
                                               (il_topLevelMain_\d+\.Emit\(OpCodes\.)Add\);
                                               \s+var (l_tmp_\d+) = new VariableDefinition\(assembly.MainModule.TypeSystem.Int32\);
                                               \s+m_topLevelStatements_\d+.Body.Variables.Add\(\2\);
                                               \s+\1Stloc, \2\);
                                               \s+\1Ldloca_S, \2\);
                                               \s+\1Call, .+ImportReference\(.+ResolveMethod\(typeof\(System.Int32\), "ToString",.+\)\)\);
                                               """));
    }

    [TestCase("""
              struct S { }

              class Foo
              {
                   S TernaryOperators(int i) => i == 2 ? new S(): new S();
              }
              """,
              """
              //Parameters of 'S TernaryOperators\(int i\) => i == 2 \? new S\(\): new S\(\);'
              \s+var p_i_4 = new ParameterDefinition\("i", ParameterAttributes.None, assembly.MainModule.TypeSystem.Int32\);
              \s+m_ternaryOperators_2.Parameters.Add\(p_i_4\);
              \s+//i == 2 \? new S\(\): new S\(\)
              \s+var lbl_conditionEnd_5 = il_ternaryOperators_3.Create\(OpCodes.Nop\);
              \s+var lbl_whenFalse_6 = il_ternaryOperators_3.Create\(OpCodes.Nop\);
              (\s+il_ternaryOperators_\d+\.Emit\(OpCodes\.)Ldarg_1\);
              \1Ldc_I4, 2\);
              \1Ceq\);
              \1Brfalse_S, lbl_whenFalse_6\);
              \s+var (l_vt_\d+) = new VariableDefinition\(st_S_0\);
              \s+m_ternaryOperators_2.Body.Variables.Add\(\2\);
              \1Ldloca_S, \2\);
              \1Initobj, st_S_0\);
              \1Ldloc, l_vt_7\);
              \1Br_S, lbl_conditionEnd_5\);
              \s+il_ternaryOperators_3.Append\(lbl_whenFalse_6\);
              \s+var (l_vt_\d+) = new VariableDefinition\(st_S_0\);
              \s+m_ternaryOperators_2.Body.Variables.Add\(\3\);
              \1Ldloca_S, \3\);
              \1Initobj, st_S_0\);
              \1Ldloc, \3\);
              \s+il_ternaryOperators_3.Append\(lbl_conditionEnd_5\);
              \1Ret\);
              """,
              TestName = "Branch/Branch used in expression introduces variable")]
    
    [TestCase("""
              struct S { }

              class Foo
              {
                   void TernaryOperators(int i) { var x = i == 2 ? new S(): new S(); }
              }
              """,
              """
              //var x = i == 2 \? new S\(\): new S\(\);
              \s+var (?<onlyVariable>l_x_\d+) = new VariableDefinition\(st_S_0\);
              \s+m_ternaryOperators_2.Body.Variables.Add\(\k<onlyVariable>\);
              \s+var (lbl_.+) = il_ternaryOperators_3.Create\(OpCodes.Nop\);
              \s+var lbl_.+ = il_ternaryOperators_3.Create\(OpCodes.Nop\);
              (\s+il_ternaryOperators_\d+\.Emit\(OpCodes\.)Ldarg_1\);
              \2Ldc_I4, 2\);
              \2Ceq\);
              \2Brfalse_S, lbl_whenFalse_7\);
              \2Ldloca_S, \k<onlyVariable>\);
              \2Initobj, st_S_0\);
              \2Br_S, lbl_conditionEnd_6\);
              \s+il_ternaryOperators_3.Append\(lbl_whenFalse_7\);
              \2Ldloca_S, \k<onlyVariable>\);
              \2Initobj, st_S_0\);
              \s+il_ternaryOperators_3.Append\(lbl_conditionEnd_6\);
              \2Ret\);
              """,
              TestName = "Branch/Branch used in variable declaration initializes declared variable")]

    [TestCase("""
              struct S { public int P { get; set; } }

              class Foo
              {
                   int TernaryOperators(int i) => i == 2 ? new S().P : new S().P;
              }
              """,
              """
              (il_ternaryOperators_\d+\.Emit\(OpCodes\.)Brfalse_S, lbl_whenFalse_\d+\);
              \s+var (?<firstVar>l_vt_\d+) = new VariableDefinition\(st_S_0\);
              \s+m_ternaryOperators_\d+.Body.Variables.Add\(\k<firstVar>\);
              (\s+\1)Ldloca_S, \k<firstVar>\);
              \2Initobj, st_S_0\);
              \2Ldloca, \k<firstVar>\);
              \2Call, m_get_\d+\);
              \2Br_S, lbl_conditionEnd_\d+\);
              \s+il_ternaryOperators_\d+.Append\(lbl_whenFalse_\d+\);
              \s+var (?<secondVar>l_vt_\d+) = new VariableDefinition\(st_S_0\);
              \s+m_ternaryOperators_\d+.Body.Variables.Add\(\k<secondVar>\);
              \2Ldloca_S, \k<secondVar>\);
              \2Initobj, st_S_0\);
              \2Ldloca, \k<secondVar>\);
              \2Call, m_get_2\);
              \s+il_ternaryOperators_\d+.Append\(lbl_conditionEnd_\d+\);
              \2Ret\);
              """,
              TestName = "Mae/Mae, introduces two variables")]
    
    [TestCase("""
              struct S { public int P { get; set; } }

              class Foo
              {
                   S M(S s) => s; 
                   S TernaryOperators(int i) => i == 2 ? new S() : M(new S());
              }
              """,
              """
              var (?<firstVar>l_vt_\d+) = new VariableDefinition\((?<structType>st_S_\d+)\);
              \s+m_ternaryOperators_\d+.Body.Variables.Add\(\k<firstVar>\);
              (\s+il_ternaryOperators_\d+\.Emit\(OpCodes\.)Ldloca_S, \k<firstVar>\);
              \1Initobj, \k<structType>\);
              \1Ldloc, \k<firstVar>\);
              \1Br_S, lbl_conditionEnd_\d+\);
              \s+il_ternaryOperators_\d+.Append\(lbl_whenFalse_\d+\);
              \1Ldarg_0\);
              \s+var (?<secondVar>l_vt_\d+) = new VariableDefinition\(\k<structType>\);
              \s+m_ternaryOperators_\d+.Body.Variables.Add\(\k<secondVar>\);
              \1Ldloca_S, \k<secondVar>\);
              \1Initobj, \k<structType>\);
              \1Ldloc, \k<secondVar>\);
              \1Call, m_M_\d+\);
              \s+il_ternaryOperators_\d+.Append\(lbl_conditionEnd_\d+\);
              \1Ret\);
              """,
              TestName = "Branch/Argument, introduces local variable")]
    
    [TestCase("""
              struct S { public int P { get; set; } }

              class Foo
              {
                   S M(S s) => s; 
                   void TernaryOperators(int i) { var l = i == 2 ? new S() : M(new S()); }
              }
              """,
              """
              (il_ternaryOperators_\d+\.Emit\(OpCodes\.)Brfalse_S, lbl_whenFalse_\d+\);
              \s+var (l_vt_\d+) = new VariableDefinition\(st_S_0\);
              \s+m_ternaryOperators_\d+.Body.Variables.Add\(\2\);
              (\s+\1)Ldloca_S, \2\);
              \3Initobj, st_S_0\);
              \3Ldloc, \2\);
              \3Br_S, lbl_conditionEnd_\d+\);
              \s+il_ternaryOperators_\d+.Append\(lbl_whenFalse_\d+\);
              \3Ldarg_0\);
              \s+var (l_vt_\d+) = new VariableDefinition\(st_S_0\);
              \s+m_ternaryOperators_\d+.Body.Variables.Add\(\4\);
              \3Ldloca_S, \4\);
              \3Initobj, st_S_0\);
              \3Ldloc, \4\);
              \3Call, m_M_\d+\);
              \s+il_ternaryOperators_\d+.Append\(lbl_conditionEnd_\d+\);
              \3Stloc, l_l_\d+\);
              """,
              TestName = "Branch/Argument, in variable declaration, introduces local variable")]
    
    [TestCase("""
              struct S { public int P { get; set; } }

              class Foo
              {
                   int TernaryOperators(int i) => new S().P == 2 ? 1 : 2;
              }
              """,
              """
              var (?<conditionVar>l_vt_\d+) = new VariableDefinition\(st_S_0\);
              \s+m_ternaryOperators_\d+.Body.Variables.Add\(\k<conditionVar>\);
              (\s+il_ternaryOperators_\d+\.Emit\(OpCodes\.)Ldloca_S, \k<conditionVar>\);
              \1Initobj, st_S_0\);
              \1Ldloca, \k<conditionVar>\);
              \1Call, m_get_2\);
              \1Ldc_I4, 2\);
              \1Ceq\);
              \1Brfalse_S, lbl_whenFalse_\d+\);
              """,
              TestName = "Target of a member access in condition")]
    
    [TestCase("""
              struct S
              {
                  public static implicit operator bool(S s) => false;
              }

              class Foo
              {
                   int TernaryOperators() => new S() ? 1 : 2;
              }
              """,
              @"//Simple value type instantiation \('new S\(\) '\) is not supported as the condition of a ternary operator in the expression: new S\(\) \? 1 : 2",
              TestName = "As condition (implicit conversion)")]
    public void InstantiatingThroughParameterlessCtor_InTernaryOperator(string source, string expected = "XXXX")
    {
        var result = RunCecilifier(source);
        
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expected));
    }
    
    [TestCaseSource(nameof(NonInstantiationValueTypeVariableInitializationTestScenarios))]
    public void NonInstantiation_ValueTypeVariableInitialization_SetsAddedVariable(string expression, string expected)
    {
        var result = RunCecilifier(
            $$"""
            struct S { }
            class Foo
            {
                S M(S s) => s;
                void Bar(S p)
                {
                    var s = {{expression}};
                }
            }
            """);
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expected));
    }
    
    [TestCase("p", TestName = "Parameter")]
    [TestCase("lr", TestName = "Local")]
    public void TestRefTarget_Struct(string target)
    {
        var result = RunCecilifier($"struct Foo {{ int value; void Bar(ref Foo p)  {{ ref Foo lr = ref p; {target}.value = 42; }} }}");
        Assert.That(
            result.GeneratedCode.ReadToEnd(),
            Does.Match(
                @"(?<il>il_bar_\d+\.Emit\(OpCodes.)(?:Ldarg_1|Ldloc,.+)\);\s+" +
                @"\k<il>Ldc_I4, 42\);\s+" +
                @"\k<il>Stfld, fld_value_1\);"));
    }

    [Test]
    public void CallOnValueType_ThroughInterface_IsConstrained()
    {
        var result = RunCecilifier("""
                                   int Call<T>(T t) where T : struct, Itf => t.M(42);
                                   
                                   interface Itf { int M(int i); } // The important part here is that M() has at least one parameter.
                                   struct Foo : Itf { public int M(int i) => i; }
                                   """);
        
        Assert.That(
            result.GeneratedCode.ReadToEnd(),
            Does.Match("""
                       (\s+il_call_15.Emit\(OpCodes\.)Ldarga, p_t_\d+\);
                       \1Ldc_I4, 42\);
                       \1Constrained, gp_T_\d+\);
                       \1Callvirt, m_M_\d+\);
                       """));
    }
    
    static IEnumerable<TestCaseData> NonInstantiationValueTypeVariableInitializationTestScenarios()
    {
        yield return new TestCaseData(
            "M(new S())",
            """
            //var s = M\(new S\(\)\);
            \s+var (l_s_\d+) = new VariableDefinition\(st_S_0\);
            \s+m_bar_5.Body.Variables.Add\(\1\);
            (\s+il_bar_6.Emit\(OpCodes\.)Ldarg_0\);
            \s+var l_vt_9 = new VariableDefinition\(st_S_0\);
            \s+m_bar_5.Body.Variables.Add\(l_vt_9\);
            \2Ldloca_S, l_vt_9\);
            \2Initobj, st_S_0\);
            \2Ldloc, l_vt_9\);
            \2Call, m_M_2\);
            \2Stloc, \1\);
            \2Ret\);
            """).SetName("From Method");
        
        yield return new TestCaseData(
            "p",
            """
            //var s = p;
            \s+var (l_s_\d+) = new VariableDefinition\(st_S_0\);
            \s+m_bar_5.Body.Variables.Add\(\1\);
            (\s+il_bar_6.Emit\(OpCodes\.)Ldarg_1\);
            \2Stloc, \1\);
            \2Ret\);
            """).SetName("From Parameter");
    }
}
