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
            var expectedSnippet = @"var (gi_M_\d+) = new GenericInstanceMethod\(r_M_\d+\).+\s+" + 
                                       @"\1.GenericArguments.Add\(assembly.MainModule.TypeSystem.Int32\);\s+";
            
            var result = RunCecilifier(code);
            Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expectedSnippet));
        }
        
        [Test]
        public void InferredTypeArgument()
        {
            var code = "class Foo { void M<T>(T t) {} void Inferred() { M(10); }  }";
            var expectedSnippet = @"var (gi_M_\d+) = new GenericInstanceMethod\(r_M_\d+\).+\s+" + 
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
                    @"var r_bar_8 = new MethodReference\(m_bar_2.Name, m_bar_2.ReturnType\).+DeclaringType = cls_foo_0.MakeGenericInstanceType\(gp_T_1\).+;\s+" +
                    @"foreach\(.+m_bar_2.GenericParameters\)\s+" +
                    @".+{\s+" +
                    @"r_bar_8.GenericParameters.Add\(.+\);\s+" +
                    @".+}\s+" +
                    @".+\s+" +
                    @"var gi_bar_9 = new GenericInstanceMethod\(r_bar_7\);\s+" +
                    @"gi_bar_9.GenericArguments.Add\(assembly.MainModule.TypeSystem.Single\);"));
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
    }
}
