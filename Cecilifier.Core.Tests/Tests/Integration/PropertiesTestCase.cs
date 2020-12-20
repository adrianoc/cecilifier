using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture]
    public class PropertiesTestCase : IntegrationResourceBasedTest
    {
        [TestCase("SimpleProperty")]
        [TestCase("SimpleAutoProperty")]
        [TestCase("SimpleStaticProperty")]
        [TestCase("Indexer")]
        [TestCase("IndexerOverloads")]
        [TestCase("PropertyAccessors")]
        public void TestProperties(string testName)
        {
            AssertResourceTest("Members/Properties/" + testName);
        }
    }
}
