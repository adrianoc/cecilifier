using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    public class GenericsTestCase : IntegrationResourceBasedTest
    {
        [TestCase("GenericOuterNonGenericInner")]
        [TestCase("GenericOuterSingleGenericInner")]
        [TestCase("GenericOuterDeepGenericInner")]
        public void TestGenericOuterAndInnerPermutations(string testName)
        {
            AssertResourceTest($"Generics/{testName}");
        }
        
        [Test]
        public void TestInstanceNonGenericMethodsOnGenericTypes()
        {
            AssertResourceTest(@"Generics/InstanceNonGenericMethodsOnGenericTypes");
        }

        [Test]
        public void TestGenericInferredStaticMethods()
        {
            AssertResourceTest(@"Generics/StaticInferredMethods");
        }

        [Test]
        public void TestGenericExplicitStaticMethods()
        {
            AssertResourceTest(@"Generics/StaticExplicitMethods");
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
    }
}
