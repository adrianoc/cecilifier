using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    public class GenericsTestCase : IntegrationResourceBasedTest
    {
        [Test]
        [Ignore("Requires Support for Generic Types")]
        public void TestGenericInstanceMethods()
        {
            AssertResourceTest(@"Generics/InstanceMethods");
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
    }
}
