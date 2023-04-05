using System.IO;
using System.Linq;
using System.Text;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture]
    public class MethodTestCase : ResourceTestBase
    {
        [Test]
        public void TestAbstractMethod()
        {
            AssertResourceTest(@"Members/Methods/AbstractMethod");
        }

        [Test]
        public void TestCtorWithParameters()
        {
            //issue: https://github.com/adrianoc/cecilifier/issues/225
            AssertResourceTest(new ResourceTestOptions { ResourceName = "Members/Methods/CtorWithParameters", FailOnAssemblyVerificationErrors = IgnoredKnownIssue.CallVirtOnValueTypes });
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
            //issue: https://github.com/adrianoc/cecilifier/issues/225
            AssertResourceTest(new ResourceTestOptions { ResourceName = $"Members/Methods/MethodCallOnValueType", FailOnAssemblyVerificationErrors = IgnoredKnownIssue.CallVirtOnValueTypes });
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
            //issue: https://github.com/adrianoc/cecilifier/issues/225
            AssertResourceTest(new ResourceTestOptions { ResourceName = $"Members/Methods/ReturnDelegate", FailOnAssemblyVerificationErrors = false });
        }

        [TestCase("Implicit")]
        [TestCase("Explicit", Ignore = "Not supported")]
        public void TestDelegateAsParameter(string implicitOrExplicit)
        {
            //issue: https://github.com/adrianoc/cecilifier/issues/225
            AssertResourceTest(new ResourceTestOptions { ResourceName = $"Members/Methods/{implicitOrExplicit}Delegate_AsParameter", FailOnAssemblyVerificationErrors = IgnoredKnownIssue.CallVirtOnValueTypes });
        }

        [TestCase("RefParamBodied", "ReturnPtrToStack")] // https://github.com/adrianoc/cecilifier/issues/227
        [TestCase("RefParam", "ReturnPtrToStack")] // https://github.com/adrianoc/cecilifier/issues/227
        [TestCase("ArrayParam")]
        [TestCase("ParamIndexer")]
        [TestCase("RefReturnField")]
        public void TestRefReturn(string test, string ignoredErrors = null)
        {
            var options = new ResourceTestOptions { ResourceName = $"Members/Methods/{test}", IgnoredILErrors = ignoredErrors };
            AssertResourceTest(options);
        }

        [Test]
        public void TestRefLocals()
        {
            AssertResourceTest("Members/Methods/RefLocals");
        }

        [Test]
        public void TestRefProperties()
        {
            AssertResourceTest("Members/Methods/RefProperties");
        }

        [Test]
        public void TestOverloads()
        {
            AssertResourceTest("Members/Methods/Overloads");
        }
    }
}
