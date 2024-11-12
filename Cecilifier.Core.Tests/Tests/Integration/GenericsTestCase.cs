using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    public class GenericsTestCase : ResourceTestBase
    {
        [TestCase("GenericOuterNonGenericInner")]
        [TestCase("GenericOuterSingleGenericInner")]
        [TestCase("GenericOuterDeepGenericInner")]
        public void TestGenericOuterAndInnerPermutations(string testName)
        {
            AssertResourceTest(new CecilifyTestOptions
            {
                ResourceName = $"Generics/{testName}",
                FailOnAssemblyVerificationErrors = IgnoredKnownIssue.MiscILVerifyVailuresNeedsInvestigation //https://github.com/adrianoc/cecilifier/issues/227
            });
        }

        [Test]
        public void TestInstanceNonGenericMethodsOnGenericTypes()
        {
            AssertResourceTest(@"Generics/InstanceNonGenericMethodsOnGenericTypes");
        }

        [Test]
        public void TestGenericInferredStaticMethods()
        {
            AssertResourceTest("Generics/StaticInferredMethods");
        }

        [Test]
        public void TestGenericExplicitStaticMethods()
        {
            AssertResourceTest("Generics/StaticExplicitMethods");
        }

        [Test]
        public void TestGenericMethodInstanceFromAssembly()
        {
            AssertResourceTest("Generics/GenericMethodsFromAssembly");
        }
        
        [Test, Ignore(reason:"Known Issue: #263")]
        public void TestMethodInvocationOnGenericParameter()
        {
            AssertResourceTest("Generics/MethodInvocationOnGenericParameter");
        }
        
        [Test]
        public void TestGenericTypesAsMembers()
        {
            AssertResourceTest(@"Generics/GenericTypesAsMembers");
        }

        [Test]
        public void TestSimplestGenericTypeDefinition()
        {
            AssertResourceTest(@"Generics/SimplestGenericTypeDefinition");
        }

        [TestCase("ExternalGenericTypeInstantiation")]
        [TestCase("GenericTypeInstantiation")]
        public void TestGenericTypeInstantiation(string testName)
        {
            AssertResourceTest($"Generics/{testName}");
        }

        [Test]
        public void TestGenericTypeDefinitionWithMembers()
        {
            AssertResourceTest(@"Generics/GenericTypeDefinitionWithMembers");
        }

        [TestCase("GenericTypesInheritance", TestName = "GenericTypesInheritance")]
        [TestCase("SimpleGenericTypeInheritance", TestName = "SimpleGenericTypeInheritance")]
        [TestCase("ComplexGenericTypeInheritance", TestName = "ComplexGenericTypeInheritance")]
        public void TestGenericTypesInheritance(string testScenario)
        {
            AssertResourceTest($"Generics/{testScenario}");
        }

        [TestCase("GenericMethods")]
        [TestCase("GenericMethodReturningGenericTypeParameter")]
        public void TestGenericMethods(string testName)
        {
            AssertResourceTest($"Generics/{testName}");
        }

        [Test]
        public void TestGenericMethodConstraints()
        {
            AssertResourceTest(@"Generics/GenericMethodConstraints");
        }

        [Test]
        public void TestGenericTypeConstraints()
        {
            AssertResourceTest(@"Generics/GenericTypeConstraints");
        }

        [Test]
        public void TestGenericTypeUsedAsConstraint()
        {
            AssertResourceTest("Generics/GenericTypeUsedAsConstraint");
        }

        [Test]
        public void TestCoContraVariance()
        {
            AssertResourceTest(@"Generics/CoContraVariance");
        }

        [Test]
        public void TestInnerTypeFromExternalAssembly()
        {
            AssertResourceTestBinary(@"Generics/InnerTypeFromExternalAssembly");
        }
        
        [Test]
        public void TestUsageOfNonGenericMethodOnGenericType()
        {
            AssertResourceTest("Generics/UsageOfNonGenericMethodOnGenericType");
        }

        [TestCase("UsageOfNonGenericMethodOnGenericTypeFromExternalAssembly")]
        [TestCase("UsageOfNonGenericMethodOnGenericTypeFromExternalAssembly2")]
        public void UsageOfNonGenericMethodOnGenericTypeFromExternalAssembly(string testName)
        {
            AssertResourceTest($"Generics/{testName}");
        }
    }
}
