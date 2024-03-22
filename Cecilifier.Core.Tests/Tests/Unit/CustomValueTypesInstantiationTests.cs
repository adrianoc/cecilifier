using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class CustomValueTypesInstantiationTests : CecilifierUnitTestBase
{
    [Test]
    public void ParameterAssignment()
    {
        var result = RunCecilifier(@"struct MyStruct { } class Foo { void M(MyStruct m) { m = new MyStruct(); } }");
        Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring(
            @"il_M_3.Emit(OpCodes.Ldarga, 0);
			il_M_3.Emit(OpCodes.Initobj, st_myStruct_0);"));
    }

    [TestCase("out")]
    [TestCase("ref")]
    public void OutRefParameterAssignment(string outRefModifier)
    {
        var result = RunCecilifier($@"struct MyStruct {{ }} class Foo {{ void M({outRefModifier} MyStruct m) {{ m = new MyStruct(); }} }}");
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(
            @"il_M_3.Emit\(OpCodes.Ldarg_1\);\s+" +
                @"il_M_3.Emit\(OpCodes.Initobj, st_myStruct_0\);"));
    }

    [TestCase("p")]
    [TestCase("default(MyStruct)", IgnoreReason = "default() not supported")]
    public void SimpleStoreToOutStructParameter(string toStore)
    {
        var result = RunCecilifier($@"struct MyStruct {{ }} class Foo {{ void M(out MyStruct m, MyStruct p) {{ m = {toStore}; }} }}");
        Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring(
            @"il_M_3.Emit(OpCodes.Ldarg_1);
			il_M_3.Emit(OpCodes.Ldarg_2);
			il_M_3.Emit(OpCodes.Stobj, st_myStruct_0);"));
    }

    [Test]
    public void LocalAssignment()
    {
        var result = RunCecilifier(@"struct MyStruct { } class Foo { void M() { MyStruct m; m = new MyStruct(); } }");
        Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring(
            @"il_M_3.Emit(OpCodes.Ldloca_S, l_m_4);
			il_M_3.Emit(OpCodes.Initobj, st_myStruct_0);"));
    }

    [Test]
    public void RefLocalAssignment()
    {
        var result = RunCecilifier(@"struct MyStruct { } class Foo { void M(MyStruct p) { ref MyStruct l = ref p; l = new MyStruct(); } }");
        Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring(
            @"	il_M_3.Emit(OpCodes.Ldloc_S, l_l_5);
			il_M_3.Emit(OpCodes.Initobj, st_myStruct_0);"));
    }

    [TestCase("p")]
    [TestCase("f")]
    public void ArrayElementAssignment(string arrayVariable)
    {
        var result = RunCecilifier($@"struct MyStruct {{ }} class Foo {{ MyStruct []f; void M(MyStruct []p) {{ {arrayVariable}[1] = new MyStruct(); }} }}");
        Assert.That(
            result.GeneratedCode.ReadToEnd(), Does.Match(
            @"il_M_4.Emit\(OpCodes.(?:Ldarg_1|Ldfld, fld_f_2)\);
			il_M_4.Emit\(OpCodes.Ldc_I4, 1\);
			il_M_4.Emit\(OpCodes.Ldelema, st_myStruct_0\);
			il_M_4.Emit\(OpCodes.Initobj, st_myStruct_0\);"));
    }
    
    [TestCaseSource(nameof(InvocationOnObjectCreationExpressionTestScenarios))]
    public void InvocationOnObjectCreationExpression(string invocationStatement, string expectedILSnippet)
    {
        var code = $$"""
                     using System;

                     {{invocationStatement}};

                     struct Test : IDisposable
                     {
                          public Test(int i) {}
                          public void M() {}
                          public void Dispose() {}
                     }
                     """;
        var result = RunCecilifier(code);
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expectedILSnippet), $"Snippet:\n{code}");
    }

    [TestCaseSource(nameof(InvocationExpressionAsParametersTestScenarios))]
    public void TestInvocationExpressionAsParameters(string testStatement, string expectedGeneratedSnippet)
    {
        var result = RunCecilifier($$"""
                                     {{testStatement}};
                                     
                                     void ByValue(Test t) {}
                                     void AsIn(in Test t) {} 
                                     
                                     struct Test 
                                     {
                                        public Test(int i) {} 
                                     }
                                     """);
        
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expectedGeneratedSnippet));
    }


    [TestCase("var x = new S(); struct S {}", 
        """
        il_topLevelMain_4.Emit\(OpCodes.Ldloca_S, l_x_7\);
        \s+il_topLevelMain_4.Emit\(OpCodes.Initobj, st_S_0\);
        """,
        TestName = "Implicit parameterless ctor")]
    
    [TestCase("var x = new S(); struct S { public S() {} }", 
        """
        (il_topLevelMain_\d+.Emit\(OpCodes\.)Newobj, ctor_S_1\);
        \s+\1Stloc, l_x_9\);
        """,
        TestName = "Explicit parameterless ctor")]
    
    [TestCase("var x = new S(42, true); struct S { public S(int i, bool b) {} }", 
        """
        (il_topLevelMain_\d+.Emit\(OpCodes\.)Ldc_I4, 42\);
        (\s+\1)Ldc_I4, 1\);
        \2Newobj, ctor_S_1\);
        """,
        TestName = "Ctor with parameters")]
    
    [TestCase("var x = new S(42, true); struct S { public S(int i, bool b) {} }", 
        """
        (il_topLevelMain_\d+.Emit\(OpCodes\.)Ldc_I4, 42\);
        (\s+\1)Ldc_I4, 1\);
        \2Newobj, ctor_S_1\);
        """,
        TestName = "Ctor with parameters")]
    public void Instantiation_EmitsCorrectOpcode(string code, string expected)
    {
        var result = RunCecilifier(code);
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expected));
    }

    static TestCaseData[] InvocationExpressionAsParametersTestScenarios()
    {
        return new[]
        {
            new TestCaseData(
                "ByValue(new Test())", 
                $"""
                 (il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldloca_S, (l_vt_\d+)\);
                 \s+\1Initobj, st_test_0\);
                 \s+\1Ldloc, \2\);
                 \s+\1Call, .+\);
                 """).SetName("by value"),
            
            new TestCaseData(
                "AsIn(new Test())", 
                $"""
                 (il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldloca_S, (l_vt_\d+)\);
                 \s+\1Initobj, st_test_0\);
                 \s+\1Ldloca, \2\);
                 \s+\1Call, .+\);
                 """).SetName("as in implicit ctor"),
            
            new TestCaseData(
                "AsIn(new Test(42))", 
                $"""
                 (il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldc_I4, 42\);
                 \s+\1Newobj, ctor_test_\d+\);
                 \s+var (l_tmp_\d+) = new VariableDefinition\(st_test_\d+\);
                 \s+m_topLevelStatements_\d+.Body.Variables.Add\(\2\);
                 \s+\1Stloc, \2\);
                 \s+\1Ldloca_S, \2\);
                 \s+\1Call, .+\);
                 """).SetName("as in explicit ctor"),
        };
    }

    static TestCaseData[] InvocationOnObjectCreationExpressionTestScenarios()
    {
        return new[]
        {
            new TestCaseData(
                "new Test().M()", 
                $"""
                 (m_topLevelStatements_\d+).Body.Variables.Add\((l_vt_\d+)\);
                 \s+(il_topLevelMain_\d+.Emit\(OpCodes\.)Ldloca_S, \2\);
                 \s+\3Initobj, st_test_0\);
                 \s+\3Ldloca, \2\);
                 \s+\3Call, m_M_\d+\);
                 \s+\1\.Body\.Instructions\.Add\(il_topLevelMain_\d+.Create\(OpCodes.Ret\)\);
                 """).SetName("Implicit: direct call own method"),
            
            new TestCaseData(
                "new Test().Dispose()",
                """
                (m_topLevelStatements_\d+).Body.Variables.Add\((l_vt_\d+)\);
                \s+(il_topLevelMain_\d+.Emit\(OpCodes\.)Ldloca_S, \2\);
                \s+\3Initobj, st_test_0\);
                \s+\3Ldloca, \2\);
                \s+\3Call, m_dispose_\d+\);
                \s+\1\.Body\.Instructions\.Add\(il_topLevelMain_\d+.Create\(OpCodes.Ret\)\);
                """).SetName("Implicit: direct interface method"),
            
            new TestCaseData(
                "((IDisposable) new Test()).Dispose()", 
                """
                (m_topLevelStatements_\d+).Body.Variables.Add\((l_vt_\d+)\);
                \s+(il_topLevelMain_\d+.Emit\(OpCodes\.)Ldloca_S, \2\);
                \s+\3Initobj, st_test_0\);
                \s+\3Ldloc, \2\);
                \s+\3Box, st_test_0\);
                \s+\3Callvirt, .+"Dispose".+\);
                \s+\1\.Body\.Instructions\.Add\(il_topLevelMain_\d+.Create\(OpCodes.Ret\)\);
                """).SetName("Implicit: call through interface cast"),
            
            new TestCaseData(
                "new Test().GetHashCode()", 
                """
                (m_topLevelStatements_\d+).Body.Variables.Add\((l_vt_\d+)\);
                \s+(il_topLevelMain_\d+.Emit\(OpCodes\.)Ldloca_S, \2\);
                \s+\3Initobj, st_test_0\);
                \s+\3Ldloca, \2\);
                \s+\3Constrained, st_test_0\);
                \s+\3Callvirt, .+GetHashCode.+\);
                \s+\3Pop\);
                \s+\1\.Body\.Instructions\.Add\(il_topLevelMain_\d+.Create\(OpCodes.Ret\)\);
                """).SetName("Implicit: direct object method call"),
            
            new TestCaseData(
                "((Object) new Test()).GetHashCode()", 
                """
                (m_topLevelStatements_\d+).Body.Variables.Add\((l_vt_\d+)\);
                \s+(il_topLevelMain_\d+.Emit\(OpCodes\.)Ldloca_S, \2\);
                \s+\3Initobj, st_test_0\);
                \s+\3Ldloc, \2\);
                \s+\3Box, st_test_0\);
                \s+\3Callvirt, .+GetHashCode.+\);
                \s+\3Pop\);
                \s+\1\.Body\.Instructions\.Add\(il_topLevelMain_\d+.Create\(OpCodes.Ret\)\);
                """).SetName("Implicit: call through object cast"),
            
            new TestCaseData(
                "((IDisposable) new Test(1)).Dispose()", 
                """
                (il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldc_I4, 1\);
                \s+\1Newobj, ctor_test_\d+\);
                \s+var (l_tmp_\d+) = new VariableDefinition\(st_test_\d+\);
                \s+m_topLevelStatements_\d+.Body.Variables.Add\(\2\);
                \s+\1Stloc, \2\);
                \s+\1Ldloc, \2\);
                \s+\1Box, st_test_\d+\);
                \s+\1Callvirt, .+"Dispose".+\);
                """).SetName("Explicit: call through interface cast"),
            
            new TestCaseData(
                "new Test(1).GetHashCode()", 
                """
                (il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldc_I4, 1\);
                \s+\1Newobj, ctor_test_\d+\);
                \s+var (l_tmp_\d+) = new VariableDefinition\(st_test_\d+\);
                \s+m_topLevelStatements_\d+.Body.Variables.Add\(\2\);
                \s+\1Stloc, \2\);
                \s+\1Ldloca_S, \2\);
                \s+\1Constrained, st_test_\d+\);
                \s+\1Callvirt, .+"GetHashCode".+\);
                \s+\1Pop\);
                """).SetName("Explicit: direct object method call"),
            
            new TestCaseData(
                "((object)new Test(1)).GetHashCode()", 
                """
                (il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldc_I4, 1\);
                \s+\1Newobj, ctor_test_\d+\);
                \s+var (l_tmp_\d+) = new VariableDefinition\(st_test_\d+\);
                \s+m_topLevelStatements_\d+.Body.Variables.Add\(\2\);
                \s+\1Stloc, \2\);
                \s+\1Ldloc, \2\);
                \s+\1Box, st_test_\d+\);
                \s+\1Callvirt, .+"GetHashCode".+\);
                \s+\1Pop\);
                """).SetName("Explicit: call through object cast"),
            
            new TestCaseData(
                "new Test(1).M()", 
                $"""
                 (il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldc_I4, 1\);
                 \s+\1Newobj, ctor_test_\d+\);
                 \s+var (l_tmp_\d+) = new VariableDefinition\(st_test_\d+\);
                 \s+m_topLevelStatements_\d+.Body.Variables.Add\(\2\);
                 \s+\1Stloc, \2\);
                 \s+\1Ldloca_S, \2\);
                 \s+\1Call, m_M_\d+\);
                 \s+m_topLevelStatements_\d+\.Body\.Instructions\.Add\(il_topLevelMain_\d+.Create\(OpCodes.Ret\)\);
                 """).SetName("Explicit: direct call own method"),
            
            new TestCaseData(
                "new Test(1).Dispose()",
                """
                (il_topLevelMain_\d+\.Emit\(OpCodes\.)Ldc_I4, 1\);
                \s+\1Newobj, ctor_test_\d+\);
                \s+var (l_tmp_\d+) = new VariableDefinition\(st_test_\d+\);
                \s+m_topLevelStatements_\d+.Body.Variables.Add\(\2\);
                \s+\1Stloc, \2\);
                \s+\1Ldloca_S, \2\);
                \s+\1Call, m_dispose_\d+\);
                \s+m_topLevelStatements_\d+\.Body\.Instructions\.Add\(il_topLevelMain_\d+.Create\(OpCodes.Ret\)\);
                """).SetName("Explicit: direct interface method"),
        };
    }    
}
