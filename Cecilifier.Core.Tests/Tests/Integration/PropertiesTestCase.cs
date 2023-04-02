using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture]
    public class PropertiesTestCase : ResourceTestBase
    {
        [TestCase("SimpleProperty")]
        [TestCase("SimpleAutoProperty")]
        [TestCase("SimpleStaticProperty")]
        [TestCase("Indexer")]
        [TestCase("IndexerOverloads")]
        [TestCase("PropertyAccessors", "ClassLoadGeneral")] //https://github.com/adrianoc/cecilifier/issues/227
        public void TestProperties(string testName, string ignoredILErrors = null)
        {
            AssertResourceTest(new ResourceTestOptions
            {
                ResourceName = $"Members/Properties/{testName}", 
                IgnoredILErrors = ignoredILErrors
            });
        }
    }
}
