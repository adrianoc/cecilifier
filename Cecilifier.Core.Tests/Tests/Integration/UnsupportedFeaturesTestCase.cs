using System.IO;
using System.Text;
using Cecilifier.Core.Misc;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    /// <summary>
    ///     These tests are meant to document the list of known unsupported features and also to ensure that these are reported if used.
    /// </summary>
    [TestFixture]
    public class UnsupportedFeaturesTestCase
    {
        [TestCase("yield return 1", TestName = "YieldReturn")]
        [TestCase("yield break", TestName = "YieldBreak")]
        public void EnumeratorBlocks(string statement)
        {
            var code = new MemoryStream(Encoding.ASCII.GetBytes($"class Test {{ System.Collections.IEnumerable Do() {{ {statement}; }} }} "));
            using (var stream = Cecilifier.Process(code, new CecilifierOptions { References = ReferencedAssemblies.GetTrustedAssembliesPath() }).GeneratedCode)
            {
                var cecilifiedCode = stream.ReadToEnd();
                Assert.That(cecilifiedCode, Does.Match("Syntax 'Yield(Return|Break)Statement' is not supported"));
            }
        }

        [TestCase("var (a,b)")]
        [TestCase("(int a, bool b)")]
        public void TupleExpression(string tuple)
        {
            AssertUnsupportedFeature($"class Foo {{ public (int, bool) F() {{ return (1, true); }} void M() {{ {tuple} = F(); }} }}", "Syntax 'TupleExpression' is not supported");
        }

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
            using (var stream = Cecilifier.Process(code, new CecilifierOptions { References = ReferencedAssemblies.GetTrustedAssembliesPath() }).GeneratedCode)
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
