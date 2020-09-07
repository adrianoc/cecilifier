using System.IO;
using System.Text;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    /// <summary>
    ///     These tests are meant to document the list of known unsupported features and also to ensure that these are reported if used.
    /// </summary>
    [TestFixture]
    public class UnsupportedFeaturesTestCase
    {
        [TestCase("void Do() => System.Console.WriteLine();", TestName = "Instance method with no return")]
        [TestCase("static void Do() => System.Console.WriteLine();", TestName = "Static method with no return")]
        [TestCase("int Do() => 1;", TestName = "Instance method with return")]
        [TestCase("static int Do() => 1;", TestName = "Static method with return")]
        public void ExpressionBodiedMethods(string methodImpl)
        {
            AssertUnsupportedFeature($"class Test {{ {methodImpl} }}", "Syntax 'ArrowExpressionClause' is not supported");
        }

        [TestCase("yield return 1", TestName = "YieldReturn")]
        [TestCase("yield break", TestName = "YieldBreak")]
        public void EnumeratorBlocks(string statement)
        {
            var code = new MemoryStream(Encoding.ASCII.GetBytes($"class Test {{ System.Collections.IEnumerable Do() {{ {statement}; }} }} "));
            using (var stream = Cecilifier.Process(code, Utils.GetTrustedAssembliesPath()).GeneratedCode)
            {
                var cecilifiedCode = stream.ReadToEnd();
                Assert.That(cecilifiedCode, Does.Match("Syntax 'Yield(Return|Break)Statement' is not supported"));
            }
        }

        [TestCase("void F() { Func<int, string> f; f = i => i.ToString(); }", TestName = "Simple Lambda")]
        [TestCase("void F(Func<int, string> f) {{ F(i => i.ToString()); }}", TestName = "Lambda as param")]
        public void LambdaExpression(string lambda)
        {
            AssertUnsupportedFeature($"using System; class Test {{ {lambda} }} ", "Syntax 'SimpleLambdaExpression' is not supported");
        }

        [TestCase("var (a,b)")]
        [TestCase("(int a, bool b)")]
        public void TupleExpression(string tuple)
        {
            AssertUnsupportedFeature($"class Foo {{ public (int, bool) F() {{ return (1, true); }} void M() {{ {tuple} = F(); }} }}", "Syntax 'TupleExpression' is not supported");
        }

        [TestCase("class C { string F(object o) { return o is string s ? s : null; } }", "IsPatternExpression", TestName = "IsPattern")]
        [TestCase("class C { string F(int i) { return $\"{i}\"; } }", "InterpolatedStringExpression", TestName = "StringInterpolation")]
        [TestCase("class C { ref object ByRef(ref object o) {return ref o; } }", "RefExpression", TestName = "RefExpression")]
        public void TestVariousExpressions(string code, string expectedComment)
        {
            AssertUnsupportedFeature(code, $"Syntax '{expectedComment}' is not supported");
        }

        [TestCase("foreach(var i in ints);", "ForEachStatement", TestName = "ForEachStatement")]
        [TestCase("while(true);", "WhileStatement", TestName = "WhileStatement")]
        [TestCase("lock(ints) {}", "LockStatement", TestName = "LockStatement")]
        [TestCase("unsafe {}", "UnsafeStatement", TestName = "UnsafeStatement")]
        public void TestStatements(string statement, string expectedInError)
        {
            AssertUnsupportedFeature($"class C {{ void F(int []ints) {{ {statement} }} }}", $"Syntax '{expectedInError}' is not supported");
        }

        private static void AssertUnsupportedFeature(string codeString, string expectedMessage)
        {
            var code = new MemoryStream(Encoding.ASCII.GetBytes(codeString));
            using(var stream = Cecilifier.Process(code, Utils.GetTrustedAssembliesPath()).GeneratedCode)
            {
                var cecilifiedCode = stream.ReadToEnd();
                Assert.That(cecilifiedCode, Contains.Substring(expectedMessage));
            }
        }

        [Test]
        public void AwaitExpression()
        {
            AssertUnsupportedFeature("class Foo { public async System.Threading.Tasks.Task<int> F(System.IO.Stream s, byte []b) { await s.ReadAsync(b); return 1; } }", "Syntax 'AwaitExpression' is not supported");
        }
    }
}
