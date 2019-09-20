using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    public class GenericsTestCase : IntegrationResourceBasedTest
    {
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
    }
}
