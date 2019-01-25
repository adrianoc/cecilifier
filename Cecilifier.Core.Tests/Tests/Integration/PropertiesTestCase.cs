using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture]
    public class PropertiesTestCase : IntegrationResourceBasedTest
    {
        [TestCase("SimpleProperty")]
        [TestCase("SimpleAutoProperty")]
        [TestCase("Indexer")]
        [TestCase("IndexerOverloads")]
        [TestCase("PropertyAccessors")]
        public void TestProperties(string testName)
        {
            AssertResourceTest("Properties/" + testName);
        }
    }
}