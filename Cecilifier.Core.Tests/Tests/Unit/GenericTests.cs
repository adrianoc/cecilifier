using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    [TestFixture]
    public class GenericTests : CecilifierUnitTestBase
    {
        [Test]
        public void ExplicitTypeArgument()
        {
            var code = "class Foo { void M<T>() {} void Explicit() { M<int>(); }  }";
            var expectedSnippet = @"var (gi_M_\d+) = new GenericInstanceMethod\(m_M_\d+\).+\s+" +
                                       @"\1.GenericArguments.Add\(assembly.MainModule.TypeSystem.Int32\);\s+";

            var result = RunCecilifier(code);
            Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expectedSnippet));
        }

        [Test]
        public void InferredTypeArgument()
        {
            var code = "class Foo { void M<T>(T t) {} void Inferred() { M(10); }  }";
            var expectedSnippet = @"var (gi_M_\d+) = new GenericInstanceMethod\(m_M_\d+\).+\s+" +
                                  @"\1.GenericArguments.Add\(assembly.MainModule.TypeSystem.Int32\);\s+";

            var result = RunCecilifier(code);
            Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expectedSnippet));
        }

        [Test]
        public void CallGenericMethodWithParameters_Issue_168()
        {
            var code = @"
                class Foo<T> 
                { 
                    void Bar<R>(R r) { }
                    void CallBar() { Bar(true); } 
                }";

            var result = RunCecilifier(code);
            Assert.That(
                result.GeneratedCode.ReadToEnd(),
                Does.Match(
                    @"//Bar\(true\);\s+" +
                    @"il_callBar_7.Emit\(OpCodes.Ldarg_0\);\s+" +
                    @"var (r_bar_\d+) = new MethodReference\(m_bar_2.Name, m_bar_2.ReturnType\).+DeclaringType = cls_foo_0.MakeGenericInstanceType\(gp_T_1\).+;\s+" +
                    @"foreach\(.+m_bar_2.Parameters\)\s+" +
                    @".+{\s+" +
                    @"\1.Parameters.Add\(new ParameterDefinition\(p.Name, p.Attributes, p.ParameterType\)\);\s+" +
                    @".+}\s+" +
                    @"foreach\(.+m_bar_2.GenericParameters\)\s+" +
                    @".+{\s+" +
                    @"\1.GenericParameters.Add\(.+\);\s+" +
                    @".+}\s+" +
                    @".+\s+" +
                    @"var (gi_bar_\d+) = new GenericInstanceMethod\(r_bar_\d+\);\s+" +
                    @"\2.GenericArguments.Add\(assembly.MainModule.TypeSystem.Boolean\);"));
        }

        [Test]
        public void CallGenericMethodWithoutParameters_Issue_168()
        {
            var code = @"
                class Foo<T> 
                { 
                    void Bar<R>() { }
                    void CallBar() { Bar<float>(); } 
                }";

            var result = RunCecilifier(code);
            Assert.That(
                result.GeneratedCode.ReadToEnd(),
                Does.Match(
                    @"//Bar<float>\(\);\s+" +
                    @"il_callBar_6.Emit\(OpCodes.Ldarg_0\);\s+" +
                    @"var r_bar_7 = new MethodReference\(m_bar_2.Name, m_bar_2.ReturnType\).+DeclaringType = cls_foo_0.MakeGenericInstanceType\(gp_T_1\).+;\s+" +
                    @"foreach\(.+m_bar_2.GenericParameters\)\s+" +
                    @".+{\s+" +
                    @"r_bar_7.GenericParameters.Add\(.+\);\s+" +
                    @".+}\s+" +
                    @".+\s+" +
                    @"var gi_bar_8 = new GenericInstanceMethod\(r_bar_7\);\s+" +
                    @"gi_bar_8.GenericArguments.Add\(assembly.MainModule.TypeSystem.Single\);"));
        }

        [Test]
        public void Issue_169_ReferenceToForwardedMethodContainingGenericParameters()
        {
            var code = @"class Foo<T> { void Bar(T t) { M(t); } T M(T t) { T tl = t; t = tl; return t; } }";
            var result = RunCecilifier(code);

            var cecilifiedCode = result.GeneratedCode.ReadToEnd();

            Assert.That(
                cecilifiedCode,
                Does.Match(
                    @"var m_M_5 = new MethodDefinition\(""M"", MethodAttributes.Private, assembly.MainModule.TypeSystem.Void\);\s+" +
                    @"m_M_5.ReturnType = gp_T_1;\s+" +
                    @"var p_t_6 = new ParameterDefinition\(""t"", ParameterAttributes.None, gp_T_1\);\s+" +
                    @"m_M_5.Parameters.Add\(p_t_6\);\s+" +
                    @"var r_M_7 = new MethodReference\(m_M_5.Name, m_M_5.ReturnType\).+;"));

            Assert.That(cecilifiedCode, Contains.Substring("il_M_9.Emit(OpCodes.Starg_S, p_t_6);")); // t = tl; ensures that the forwarded parameters has been used in M()'s implementation
        }

        [TestCase(@"class Foo<T> where T : new() { T M() => new T(); }")]
        [TestCase(@"class Foo { T M<T>() where T : new() => new T(); }")]
        public void TestInstantiatingGenericTypeParameter(string code)
        {
            var result = RunCecilifier(code);
            var cecilifiedCode = result.GeneratedCode.ReadToEnd();

            Assert.That(cecilifiedCode, Does.Match(@"il_M_\d+.Emit\(OpCodes.Call, .+ImportReference\(typeof\(System\.Activator\)\)\.MakeGenericInstanceType\(gp_T_\d+\)\);"));
            Assert.That(cecilifiedCode, Does.Match(@"m_M_\d+\.ReturnType = gp_T_\d+;"));
        }

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
        public void TestForwardReferencedMethodGenericTypeParameter_Issue240()
        {
            var code = """
                       class C 
                       {
                           bool Run() => M(1, 2); // References method 'M<T>()' before its declaration. 
                           bool M<T>(T other, System.IEquatable<T> t) => false; 
                       }
                       """;

            var result = RunCecilifier(code);

            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            Assert.That(cecilifiedCode, Does.Match("""var gp_T_\d+ = new Mono.Cecil.GenericParameter\("T", m_M_\d+\);"""), "Generic type parameter (T) not defined.");
            Assert.That(cecilifiedCode, Does.Match("""var p_other_\d+ = new ParameterDefinition\("other", ParameterAttributes.None, gp_T_\d+\);"""), "definition of parameter 'other' does not match.");
            Assert.That(cecilifiedCode, Does.Match("""var p_t_\d+ = new ParameterDefinition\("t", .+, assembly.MainModule.ImportReference\(typeof\(System.IEquatable<>\)\).MakeGenericInstanceType\(gp_T_\d+\)\);"""), "Generic parameter type does not match.");
        }
        
        [Test]
        public void TestParameterOfGenericType_DependingOnGenericTypeParameterOfMethod_Issue240()
        {
            var code = "class C { bool M<T>(T other, System.IEquatable<T> t) => t.Equals(other); }";
            var result = RunCecilifier(code);

            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            
            Assert.That(cecilifiedCode, Does.Match("""var l_openEquals_\d+ = .+ImportReference\(typeof\(System.IEquatable<>\)\).Resolve\(\).Methods.First\(m => m.Name == "Equals"\);"""), "method from open generic type not match");
            Assert.That(cecilifiedCode, Does.Match("""var r_equals_\d+ = new MethodReference\("Equals", assembly.MainModule.ImportReference\(l_openEquals_\d+\).ReturnType\)"""), "MethodReference does not match");
            Assert.That(cecilifiedCode, Does.Match("""il_M_3.Emit\(OpCodes.Callvirt, r_equals_\d+\);"""), "Call to the target method does not match");
        }
        
        [Test]
        public void GenericParameter_IsUsed_InsteadOfTypeOf_Issue240()
        {
            /*
             * This test ensures that
             * 1. return type depending on generic method type parameter (`T` in IEnumerator<T> M<T>(...)) generates correct code, i.e, references the GenericParameter()
             *    instantiated to represent `T`  instead of `typeof(T)`.
             * 2. resolution of the target method, i.e, the call to `GetEnumerator()` is emitted using the same GenericParameter() as described above instead of `typeof(T)`
             */
            var code = "using System.Collections.Generic; class C { IEnumerator<T> M<T>(IEnumerable<T> e) => e.GetEnumerator(); }";
            var result = RunCecilifier(code);

            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            
            Assert.That(cecilifiedCode, Does.Not.Match(@"typeof\(T\)"), "`T` should be referenced through a GenericParameter() instead.");
            Assert.That(cecilifiedCode, Does.Match(@"m_M_\d+.ReturnType = .+ImportReference\(typeof\(.+IEnumerator<>\)\).MakeGenericInstanceType\(gp_T_\d+\);"), "Return type should reference the GenericParameter().");
            Assert.That(cecilifiedCode, Does.Match("""
                                                   \s+var r_getEnumerator_7 = new MethodReference\("GetEnumerator", assembly.MainModule.ImportReference\(l_openGetEnumerator_6\).ReturnType\)
                                                   \s+{
                                                   \s+DeclaringType = l_iEnumerable_5,
                                                   \s+HasThis = l_openGetEnumerator_6.HasThis,
                                                   \s+ExplicitThis = l_openGetEnumerator_6.ExplicitThis,
                                                   \s+CallingConvention = l_openGetEnumerator_6.CallingConvention,
                                                   \s+};
                                                   \s+il_M_3.Emit\(OpCodes.Callvirt, r_getEnumerator_7\);
                                                   """), "Target of call instruction validation.");
        }

        [TestCase("bool bx = value != null", """
                                      //bool bx = value != null;
                                      \s+var l_bx_13 = new VariableDefinition\(assembly.MainModule.TypeSystem.Boolean\);
                                      \s+m_test_6.Body.Variables.Add\(l_bx_13\);
                                      (\s+il_test_\d+\.Emit\(OpCodes\.)Ldarg_1\);
                                      \1Box, gp_T_\d+\);
                                      \1Ldnull\);
                                      \1Cgt\);
                                      \1Stloc, l_bx_13\);
                                      """, IgnoreReason = "Issue #242")]
        
        [TestCase("bool bx; bx = value != null", """
                                      //bx = value != null;
                                      (\s+il_test_\d+\.Emit\(OpCodes\.)Ldarg_1\);
                                      \1Box, gp_T_\d+\);
                                      \1Ldnull\);
                                      \1Cgt\);
                                      \1Stloc, l_bx_13\);
                                      \1Ret\);
                                      """, IgnoreReason = "Issue #242")]
        
        [TestCase(
            "bool b; b = value is string", 
            """
            //b = value is string;
            (\s+il_test_\d+\.Emit\(OpCodes\.)Ldarg_1\);
            \1Box, gp_T_\d+\);
            \1Isinst, assembly.MainModule.TypeSystem.String\);
            \1Ldnull\);
            \1Cgt\);
            \1Stloc, l_b_\d+\);
            \1Ret\);
            """
            )]
        
        [TestCase("object o = value", """
                                      //object o = value;
                                      \s+var (l_o_\d+) = new VariableDefinition\(assembly.MainModule.TypeSystem.Object\);
                                      \s+m_test_6.Body.Variables.Add\(\1\);
                                      (\s+il_test_\d+\.Emit\(OpCodes\.)Ldarg_1\);
                                      \2Box, gp_T_7\);
                                      \2Stloc, \1\);
                                      \2Ret\);
                                      """)]
        
        [TestCase(
            "object o; o = value", 
            """
            //o = value;
            (\s+il_test_\d+\.Emit\(OpCodes\.)Ldarg_1\);
            \1Box, gp_T_\d+\);
            \1Stloc, l_o_\d+\);
            """)]
        
        [TestCase(
            "paramIDisp = value", 
            """
            //paramIDisp = value;
            (\s+il_test_\d+\.Emit\(OpCodes\.)Ldarg_1\);
            \1Box, gp_T_\d+\);
            \1Starg_S, p_paramIDisp_\d+\);
            """)]

        [TestCase(
            "localIDisp = value", 
            """
            //localIDisp = value;
            (\s+il_test_\d+\.Emit\(OpCodes\.)Ldarg_1\);
            \1Box, gp_T_\d+\);
            \1Stloc, l_localIDisp_\d+\);
            """)]
        
        [TestCase(
            "fieldIDisp = value", 
            """
            //fieldIDisp = value;
            (\s+il_test_\d+\.Emit\(OpCodes\.)Ldarg_0\);
            \1Ldarg_1\);
            \1Box, gp_T_\d+\);
            \1Stfld, fld_fieldIDisp_\d+\);
            """)]
        
        [TestCase(
            "object o; o = M<T>(value)", 
            """
            //o = M<T>\(value\);
            (\s+il_test_\d+\.Emit\(OpCodes\.)Ldarg_0\);
            \s+var (gi_M_\d+) = new GenericInstanceMethod\(m_M_\d+\);
            \s+gi_M_\d+.GenericArguments.Add\((gp_T_\d+)\);
            \1Ldarg_1\);
            \1Call, \2\);
            \1Box, \3\);
            \1Stloc, l_o_\d+\);
            """)]
        
        [TestCase(
            """object o; o = M<string>("Ola Mundo")""", 
            """
            //o = M<string>\("Ola Mundo"\);
            (\s+il_test_\d+\.Emit\(OpCodes\.)Ldarg_0\);
            \s+var (gi_M_\d+) = new GenericInstanceMethod\(m_M_\d+\);
            \s+gi_M_\d+.GenericArguments.Add\(assembly.MainModule.TypeSystem.String\);
            \1Ldstr, "Ola Mundo"\);
            \1Call, \2\);
            \1Stloc, l_o_\d+\);
            """)]
        
        [TestCase(
            """Foo f = new Foo(); object o; o = f.M<T>(value)""", 
            """
            //o = f.M<T>\(value\);
            (\s+il_test_\d+\.Emit\(OpCodes\.)Ldloc, l_f_\d+\);
            \s+var (gi_M_\d+) = new GenericInstanceMethod\(m_M_\d+\);
            \s+\2.GenericArguments.Add\((gp_T_\d+)\);
            \1Ldarg_1\);
            \1Callvirt, \2\);
            \1Box, \3\);
            \1Stloc, l_o_\d+\);
            """)]
        
        [TestCase(
            "Test(value, paramIDisp, value, 42);", 
            """
            //Test\(value, paramIDisp, value, 42\);
            (\s+il_test_\d+.Emit\(OpCodes\.)Ldarg_0\);
            \s+var gi_test_\d+ = new GenericInstanceMethod\(m_test_\d+\);
            \s+gi_test_\d+.GenericArguments.Add\(gp_T_\d+\);
            \s+gi_test_\d+.GenericArguments.Add\(assembly.MainModule.TypeSystem.Int32\);
            \1Ldarg_1\);
            \1Ldarg_2\);
            \1Ldarg_1\);
            \1Box, gp_T_\d+\);
            \1Ldc_I4, 42\);
            \1Call, gi_test_\d+\);
            """)]
        
        [TestCase(
            "TU ltu; ltu = ptu;", 
            """
            //ltu = ptu;
            (\s+il_test_\d+\.Emit\(OpCodes\.)Ldarg, 4\);
            \1Stloc, l_ltu_\d+\);
            """, TestName = "Unconstrained: Assignment to local do not box")]
        
        [TestCase(
            "T lt; lt = value;", 
            """
            //lt = value;
            (\s+il_test_\d+\.Emit\(OpCodes\.)Ldarg_1\);
            \1Stloc, l_lt_\d+\);
            """, TestName = "Constrained: Assignment to local do not box")]
        
        [TestCase(
            "T lt = value;", 
            """
            //T lt = value;
            \s+var (l_lt_\d+) = new VariableDefinition\(gp_T_\d+\);
            \s+m_test_\d+.Body.Variables.Add\(\1\);
            (\s+il_test_\d+\.Emit\(OpCodes\.)Ldarg_1\);
            \2Stloc, l_lt_\d+\);
            """, TestName = "Constrained: Local variable initializer does not box")]
        public void GenericType_Boxing(string statementToUse, string expectedILSnippet)
        {
            var result = RunCecilifier($$"""
                                         using System;
                                         class Foo
                                         {
                                             private IDisposable fieldIDisp; 
                                             
                                             T M<T>(T t) => t;
                                             
                                             void Test<T, TU>(T value, IDisposable paramIDisp, object paramObj, TU ptu) where T : class, IDisposable
                                             {
                                                IDisposable localIDisp = null;
                                                {{statementToUse}};
                                             }
                                         }
                                         """);
                
            Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expectedILSnippet));
        }
    }
}
