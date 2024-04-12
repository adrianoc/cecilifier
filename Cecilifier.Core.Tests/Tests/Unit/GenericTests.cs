using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit
{
    /*
     * Some generic types/methods related features are covered in other tests (for example,
     * generic local functions are covered in LocalFunctionTests.cs)
     */
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
                    @"il_callBar_7.Emit\(OpCodes.Ldc_I4, 1\);\s+" + 
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
                    @"var (gi_bar_\d+) = new GenericInstanceMethod\(\1\);\s+" +
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
                    @"il_bar_3.Emit\(OpCodes.Ldarg_1\);\s+" +
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
            \1Ldarg_1\);
            \s+var (gi_M_\d+) = new GenericInstanceMethod\(m_M_\d+\);
            \s+gi_M_\d+.GenericArguments.Add\((gp_T_\d+)\);
            \1Call, \2\);
            \1Box, \3\);
            \1Stloc, l_o_\d+\);
            """)]
        
        [TestCase(
            """object o; o = M<string>("Ola Mundo")""", 
            """
            //o = M<string>\("Ola Mundo"\);
            (\s+il_test_\d+\.Emit\(OpCodes\.)Ldarg_0\);
            \1Ldstr, "Ola Mundo"\);
            \s+var (gi_M_\d+) = new GenericInstanceMethod\(m_M_\d+\);
            \s+gi_M_\d+.GenericArguments.Add\(assembly.MainModule.TypeSystem.String\);
            \1Call, \2\);
            \1Stloc, l_o_\d+\);
            """)]
        
        [TestCase(
            """Foo f = new Foo(); object o; o = f.M<T>(value)""", 
            """
            //o = f.M<T>\(value\);
            (\s+il_test_\d+\.Emit\(OpCodes\.)Ldloc, l_f_\d+\);
            \1Ldarg_1\);
            \s+var (gi_M_\d+) = new GenericInstanceMethod\(m_M_\d+\);
            \s+\2.GenericArguments.Add\((gp_T_\d+)\);
            \1Callvirt, \2\);
            \1Box, \3\);
            \1Stloc, l_o_\d+\);
            """)]
        
        [TestCase(
            "Test(value, paramIDisp, value, 42);", 
            """
            //Test\(value, paramIDisp, value, 42\);
            (\s+il_test_\d+.Emit\(OpCodes\.)Ldarg_0\);
            \1Ldarg_1\);
            \1Ldarg_2\);
            \1Ldarg_1\);
            \1Box, gp_T_\d+\);
            \1Ldc_I4, 42\);
            \s+var (gi_test_\d+) = new GenericInstanceMethod\(m_test_\d+\);
            \s+\2.GenericArguments.Add\(gp_T_\d+\);
            \s+\2.GenericArguments.Add\(assembly.MainModule.TypeSystem.Int32\);
            \1Call, \2\);
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
        
        [TestCase(
            "static T1 M<T1>(T1[] a) where T1 : Foo => a[0]", 
            """
            (\s+il_M_\d+\.Emit\(OpCodes\.)Ldarg_0\);
            \1Ldc_I4, 0\);
            \1Ldelem_Any, gp_t1_\d+\);
            \1Ret\);
            """, TestName = "Array")]
        public void GenericType_Boxing(string statementToUse, [StringSyntax(StringSyntaxAttribute.Regex)] string expectedILSnippet)
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

        [Test]
        public void GenericInstanceMethods_AreCached()
        {
            var result = RunCecilifier("""
                                       using System;
                                       
                                       var sa1 = M<string>();
                                       var sa2 = M<string>();
                                       var ia1 = M<int>();
                                       
                                       T M<T>() => default(T);
                                       """);

            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            
            Assert.Multiple(() =>
            {
                var stringVersionCount = Regex.Matches(cecilifiedCode, """
                                                                       \s+var gi_M_\d+ = new GenericInstanceMethod\(m_M_7\);
                                                                       \s+gi_M_\d+.GenericArguments.Add\(assembly.MainModule.TypeSystem.String\);
                                                                       """);
                Assert.That(stringVersionCount.Count, Is.EqualTo(1), cecilifiedCode);

                var intVersionCount = Regex.Matches(cecilifiedCode, """
                                                                    \s+var gi_M_\d+ = new GenericInstanceMethod\(m_M_7\);
                                                                    \s+gi_M_\d+.GenericArguments.Add\(assembly.MainModule.TypeSystem.Int32\);
                                                                    """);
                Assert.That(intVersionCount.Count, Is.EqualTo(1), cecilifiedCode);
            });
        }

        [TestCase(
            "static int M<T>(T[] b) where T : Foo => b[0].data;",
            """
            (\s+il_M_16\.Emit\(OpCodes\.)Ldarg_0\);
            \1Ldc_I4, 0\);
            \1Ldelem_Any, gp_T_\d+\);
            \1Box, gp_T_\d+\);
            \1Ldfld, fld_data_\d+\);
            """,
            TestName = "Array")]
        
        [TestCase("static int M<T>(List<T> b) where T : Foo => b[0].data;",
            """
            (\s+il_M_\d+\.Emit\(OpCodes\.)Callvirt, r_get_Item_\d+\);
            \1Box, gp_T_\d+\);
            \1Ldfld, fld_data_\d+\);
            """,
            TestName = "List<T>")]
        
        [TestCase("static int M<T>(IList<T> b) where T : Foo => b[0].data;",
            """
            (\s+il_M_\d+\.Emit\(OpCodes\.)Callvirt, r_get_Item_\d+\);
            \1Box, gp_T_\d+\);
            \1Ldfld, fld_data_\d+\);
            """,
            TestName = "IList<T>")]
        
        [TestCase("static int M<T>(IEnumerator<T> b) where T : Foo => b.Current.data;",
            """
            (\s+il_M_\d+\.Emit\(OpCodes\.)Callvirt, r_get_Current_\d+\);
            \1Box, gp_T_\d+\);
            \1Ldfld, fld_data_\d+\);
            """,
            TestName = "IEnumerator<T>")]
        public void MemberAccess_OnGenericCollections(string snippet, string expectedIL)
        {
            var result = RunCecilifier($$"""
                                       using System.Collections.Generic;
                                       {{snippet}}
                                       
                                       class Foo
                                       {
                                         public int data;
                                       }
                                       
                                       [System.Runtime.CompilerServices.InlineArray(2)]
                                       struct Buffer<T>
                                       {
                                       	private T _data;
                                       }
                                       """);

            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            Assert.That(cecilifiedCode, Does.Match(expectedIL));
        }

        [TestCaseSource(nameof(Scenarios))]
        public void GenericMethod_Overloads(string snippet, string expectedIL)
        {
            var result = RunCecilifier($$"""
                                       class Foo
                                       {
                                       	   void M<T>() {}
                                       	   {{snippet}}
                                       }
                                       """);

            var cecilifiedCode = result.GeneratedCode.ReadToEnd();
            Assert.That(cecilifiedCode, Does.Match(expectedIL));
        }

        static TestCaseData[] Scenarios()
        {
            return
            [
                new TestCaseData("void M() { M(); }", """
                                                      \s+//Method : M
                                                      \s+var (?<generic>m_M_\d+) = new MethodDefinition\("M",.+\);
                                                      \s+var gp_T_2 = new Mono.Cecil.GenericParameter\("T", \k<generic>\);
                                                      \s+\k<generic>.GenericParameters.Add\(gp_T_2\);
                                                      \s+cls_foo_0.Methods.Add\(\k<generic>\);
                                                      \s+\k<generic>.Body.InitLocals = true;
                                                      \s+var il_M_\d+ = \k<generic>.Body.GetILProcessor\(\);
                                                      \s+il_M_\d+.Emit\(OpCodes.Ret\);
                                                      \s+//Method : M
                                                      \s+var (?<test_method>m_M_\d+) = new MethodDefinition\("M", .+\);
                                                      \s+cls_foo_0.Methods.Add\(\k<test_method>\);
                                                      \s+\k<test_method>.Body.InitLocals = true;
                                                      \s+var il_M_\d+ = \k<test_method>.Body.GetILProcessor\(\);
                                                      \s+//M\(\);
                                                      (\s+il_M_\d+.Emit\(OpCodes.)Ldarg_0\);
                                                      \1Call, \k<test_method>\);
                                                      \1Ret\);
                                                      """)
                    .SetName("Non Generic - Recursive call"),
                
                new TestCaseData("void M<T,S>() { M<T,S>(); }", """
                                                      \s+//Method : M
                                                      \s+var (?<g1>m_M_\d+) = new MethodDefinition\("M",.+\);
                                                      \s+var gp_T_2 = new Mono.Cecil.GenericParameter\("T", \k<g1>\);
                                                      \s+\k<g1>.GenericParameters.Add\(gp_T_2\);
                                                      \s+cls_foo_0.Methods.Add\(\k<g1>\);
                                                      \s+\k<g1>.Body.InitLocals = true;
                                                      \s+var il_M_3 = \k<g1>.Body.GetILProcessor\(\);
                                                      \s+il_M_3.Emit\(OpCodes.Ret\);
                                                      \s+//Method : M
                                                      \s+var (?<test_method>m_M_\d+) = new MethodDefinition\("M",.+\);
                                                      \s+var gp_T_5 = new Mono.Cecil.GenericParameter\("T", \k<test_method>\);
                                                      \s+var gp_S_6 = new Mono.Cecil.GenericParameter\("S", \k<test_method>\);
                                                      \s+\k<test_method>.GenericParameters.Add\(gp_T_5\);
                                                      \s+\k<test_method>.GenericParameters.Add\(gp_S_6\);
                                                      \s+cls_foo_0.Methods.Add\(\k<test_method>\);
                                                      \s+\k<test_method>.Body.InitLocals = true;
                                                      \s+var il_M_7 = \k<test_method>.Body.GetILProcessor\(\);
                                                      \s+//M<T,S>\(\);
                                                      \s+il_M_7.Emit\(OpCodes.Ldarg_0\);
                                                      \s+var gi_M_8 = new GenericInstanceMethod\(\k<test_method>\);
                                                      \s+gi_M_8.GenericArguments.Add\(gp_T_5\);
                                                      \s+gi_M_8.GenericArguments.Add\(gp_S_6\);
                                                      \s+il_M_7.Emit\(OpCodes.Call, gi_M_8\);
                                                      \s+il_M_7.Emit\(OpCodes.Ret\);
                                                      """)
                    .SetName("Generic - Recursive"),
                
                new TestCaseData("void M<T,S>() { M<T>(); }", """
                                                              \s+//Method : M
                                                              \s+var (?<g1>m_M_\d+) = new MethodDefinition\("M",.+\);
                                                              \s+var gp_T_2 = new Mono.Cecil.GenericParameter\("T", \k<g1>\);
                                                              \s+\k<g1>.GenericParameters.Add\(gp_T_2\);
                                                              \s+cls_foo_0.Methods.Add\(\k<g1>\);
                                                              \s+\k<g1>.Body.InitLocals = true;
                                                              \s+var il_M_3 = \k<g1>.Body.GetILProcessor\(\);
                                                              \s+il_M_3.Emit\(OpCodes.Ret\);
                                                              \s+//Method : M
                                                              \s+var (?<test_method>m_M_\d+) = new MethodDefinition\("M",.+\);
                                                              \s+var gp_T_5 = new Mono.Cecil.GenericParameter\("T", \k<test_method>\);
                                                              \s+var gp_S_6 = new Mono.Cecil.GenericParameter\("S", \k<test_method>\);
                                                              \s+\k<test_method>.GenericParameters.Add\(gp_T_5\);
                                                              \s+\k<test_method>.GenericParameters.Add\(gp_S_6\);
                                                              \s+cls_foo_0.Methods.Add\(\k<test_method>\);
                                                              \s+\k<test_method>.Body.InitLocals = true;
                                                              \s+var il_M_7 = \k<test_method>.Body.GetILProcessor\(\);
                                                              \s+//M<T>\(\);
                                                              \s+il_M_7.Emit\(OpCodes.Ldarg_0\);
                                                              \s+var gi_M_8 = new GenericInstanceMethod\(\k<g1>\);
                                                              \s+gi_M_8.GenericArguments.Add\(gp_T_5\);
                                                              \s+il_M_7.Emit\(OpCodes.Call, gi_M_8\);
                                                              \s+il_M_7.Emit\(OpCodes.Ret\);
                                                              """)
                    .SetName("Generic - Calls overload"),
                
                new TestCaseData("class Inner { void M<T>() => M<T>(); }", """
                                                              \s+//Method : M
                                                              \s+var (?<overload>m_M_\d+) = new MethodDefinition\("M",.+\);
                                                              \s+var gp_T_6 = new Mono.Cecil.GenericParameter\("T", \k<overload>\);
                                                              \s+\k<overload>.GenericParameters.Add\(gp_T_6\);
                                                              \s+cls_inner_\d+.Methods.Add\(\k<overload>\);
                                                              \s+\k<overload>.Body.InitLocals = true;
                                                              \s+var il_M_7 = \k<overload>.Body.GetILProcessor\(\);
                                                              \s+//M<T>\(\)
                                                              \s+il_M_7.Emit\(OpCodes.Ldarg_0\);
                                                              \s+var (?<gen_method>gi_M_\d+) = new GenericInstanceMethod\(\k<overload>\);
                                                              \s+\k<gen_method>.GenericArguments.Add\(gp_T_6\);
                                                              \s+il_M_7.Emit\(OpCodes.Call, \k<gen_method>\)
                                                              """)
                    .SetName("Inner Generic - Calls inner"),
            ];
        }
    }
}
