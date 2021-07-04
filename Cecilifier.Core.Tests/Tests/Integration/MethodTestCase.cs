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
            AssertResourceTest(@"Members/Methods/AbstractMethod");
        }

        [Test]
        public void TestCtorWithParameters()
        {
            AssertResourceTest(@"Members/Methods/CtorWithParameters");
        }

        [Test]
        public void TestDefaultCtorFromBaseClass()
        {
            AssertResourceTest(@"Members/Methods/DefaultCtorFromBaseClass");
        }

        [Test]
        public void TestExplicityDefaultCtor()
        {
            AssertResourceTest(@"Members/Methods/ExplicityDefaultCtor");
        }

        [Test]
        public void TestExternalMethodReference()
        {
            AssertResourceTest(@"Members/Methods/ExternalMethodReference");
        }

        [Test]
        public void TestInterfaceMethodVirtualImplementation()
        {
            AssertResourceTest(@"Members/Methods/InterfaceMethodVirtualImplementation");
        }

        [Test]
        public void TestMethodCallOnValueType()
        {
            AssertResourceTest(@"Members/Methods/MethodCallOnValueType");
        }

        [Test]
        [Combinatorial]
        public void TestMethodInvocation(
            [Values("QualifiedRecursive", "UnqualifiedRecursive")]
            string testNamePrefix,
            [Values("WithParams", "NoParams")] string testName)
        {
            AssertResourceTest($"Members/Methods/Invocation/{testNamePrefix}{testName}");
        }

        [Test]
        public void TestMultipleParameters()
        {
            AssertResourceTest(@"Members/Methods/MultipleParameters");
        }

        [Test]
        public void TestMutuallyRecursive()
        {
            AssertResourceTest(@"Members/Methods/MutuallyRecursive");
        }

        [Test]
        public void TestNoParameters()
        {
            AssertResourceTest(@"Members/Methods/NoParameters");
        }

        [Test]
        public void TestParameterModifiers()
        {
            AssertResourceTest(@"Members/Methods/ParameterModifiers");
        }
        
        [Test]
        public void TestRefParameters()
        {
            AssertResourceTest(@"Members/Methods/RefParameters");
        }

        [Test]
        public void TestReturnValue()
        {
            AssertResourceTest(@"Members/Methods/ReturnValue");
        }

        [Test]
        public void TestSelfReferencingCtor()
        {
            AssertResourceTest(@"Members/Methods/SelfRefCtor");
        }

        [Test]
        public void TestSingleSimpleParameter()
        {
            AssertResourceTest(@"Members/Methods/SingleSimpleParameter");
        }

        [Test]
        public void TestTypeWithNoArgCtorAndInnerClass()
        {
            AssertResourceTest(@"Members/Methods/TypeWithNoArgCtorAndInnerClass");
        }

        [Test]
        public void TestVariableNumberOfParameters()
        {
            AssertResourceTest(@"Members/Methods/VariableNumberOfParameters");
        }

        [Test]
        public void TestVirtualMethod()
        {
            AssertResourceTest(@"Members/Methods/VirtualMethod");
        }
        
        [Test]
        public void TestReturnDelegate()
        {
            AssertResourceTest("Members/Methods/ReturnDelegate");
        }
        
        [TestCase("Implicit")]
        [TestCase("Explicit", Ignore = "Not supported")]
        public void TestDelegateAsParameter(string implicitOrExplicit)
        {
            AssertResourceTest($"Members/Methods/{implicitOrExplicit}Delegate_AsParameter");
        }
        
        [Test]
        public void LocalVariableDeclarations_Are_CommentedOut()
        {
            AssertCecilifiedCodeContainsSnippet(
                "class C { int S(int i, int j) { int l = i / 2; return l + j; } }",
                "//int l = i / 2; ");
        }

        [TestCase("RefParamBodied")]
        [TestCase("RefParam")]
        [TestCase("ArrayParam")]
        [TestCase("ParamIndexer")]
        [TestCase("RefReturnField")]
        public void TestRefReturn(string test)
        {
            AssertResourceTest($"Members/Methods/{test}");
        }
        
        [Test]
        public void TestRefLocals()
        {
            AssertResourceTest("Members/Methods/RefLocals");
        }

        private void AssertCecilifiedCodeContainsSnippet(string code, string expectedSnippet)
        {
            var cecilifier = Cecilifier.Process(new MemoryStream(Encoding.UTF8.GetBytes(code)), Utils.GetTrustedAssembliesPath().ToArray());
            var generated = cecilifier.GeneratedCode.ReadToEnd();

            Assert.That(generated, Does.Contain(expectedSnippet), "Expected snippet not found");
        }
    }
}
