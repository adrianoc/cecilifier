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
             (?://return new Test\(\))?;
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
    public void TestX(string expression)
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
}
