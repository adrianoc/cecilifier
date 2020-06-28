using System.IO;
using System.Linq;
using System.Text;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture]
    public class MethodTestCase : IntegrationResourceBasedTest
    {
        [Test]
        public void TestAbstractMethod()
        {
            AssertResourceTest(@"Methods/AbstractMethod");
        }

        [Test]
        public void TestCtorWithParameters()
        {
            AssertResourceTest(@"Methods/CtorWithParameters");
        }

        [Test]
        public void TestDefaultCtorFromBaseClass()
        {
            AssertResourceTest(@"Methods/DefaultCtorFromBaseClass");
        }

        [Test]
        public void TestExplicityDefaultCtor()
        {
            AssertResourceTest(@"Methods/ExplicityDefaultCtor");
        }

        [Test]
        public void TestExternalMethodReference()
        {
            AssertResourceTest(@"Methods/ExternalMethodReference");
        }

        [Test]
        [Ignore("Not Implemented yet")]
        public void TestInOutRefParameters()
        {
            AssertResourceTest(@"Methods/InOutRefParameters");
        }

        [Test]
        public void TestInterfaceMethodVirtualImplementation()
        {
            AssertResourceTest(@"Methods/InterfaceMethodVirtualImplementation");
        }

        [Test]
        public void TestMethodCallOnValueType()
        {
            AssertResourceTest(@"Methods/MethodCallOnValueType");
        }

        [Test]
        [Combinatorial]
        public void TestMethodInvocation(
            [Values("QualifiedRecursive", "UnqualifiedRecursive")]
            string testNamePrefix,
            [Values("WithParams", "NoParams")] string testName)
        {
            AssertResourceTest($"Methods/Invocation/{testNamePrefix}{testName}");
        }

        [Test]
        public void TestMultipleParameters()
        {
            AssertResourceTest(@"Methods/MultipleParameters");
        }

        [Test]
        public void TestMutuallyRecursive()
        {
            AssertResourceTest(@"Methods/MutuallyRecursive");
        }

        [Test]
        public void TestNoParameters()
        {
            AssertResourceTest(@"Methods/NoParameters");
        }

        [Test]
        public void TestParameterModifiers()
        {
            AssertResourceTest(@"Methods/ParameterModifiers");
        }

        [Test]
        public void TestReturnValue()
        {
            AssertResourceTest(@"Methods/ReturnValue");
        }

        [Test]
        public void TestSelfReferencingCtor()
        {
            AssertResourceTest(@"Methods/SelfRefCtor");
        }

        [Test]
        public void TestSingleSimpleParameter()
        {
            AssertResourceTest(@"Methods/SingleSimpleParameter");
        }

        [Test]
        public void TestTypeWithNoArgCtorAndInnerClass()
        {
            AssertResourceTest(@"Methods/TypeWithNoArgCtorAndInnerClass");
        }

        [Test]
        public void TestVariableNumberOfParameters()
        {
            AssertResourceTest(@"Methods/VariableNumberOfParameters");
        }

        [Test]
        public void TestVirtualMethod()
        {
            AssertResourceTest(@"Methods/VirtualMethod");
        }
        
        [Test]
        public void LocalVariableDeclarations_Are_CommentedOut()
        {
            AssertCecilifiedCodeContainsSnippet(
                "class C { int S(int i, int j) { int l = i / 2; return l + j; } }",
                "//int l = i / 2; ");
        }

        private void AssertCecilifiedCodeContainsSnippet(string code, string expectedSnippet)
        {
            var cecilifier = Cecilifier.Process(new MemoryStream(Encoding.UTF8.GetBytes(code)), Utils.GetTrustedAssembliesPath().ToArray());
            var generated = cecilifier.GeneratedCode.ReadToEnd();

            Assert.That(generated, Does.Contain(expectedSnippet), "Expected snippet not found");
        }
    }
}
