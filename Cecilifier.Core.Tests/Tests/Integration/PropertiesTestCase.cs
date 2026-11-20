using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture(typeof(MonoCecilContext))]
    [TestFixture(typeof(SystemReflectionMetadataContext))]
    public class PropertiesTestCase<TResource> : ResourceTestBase<TResource> where TResource : IVisitorContext
    {
        [TestCase("SimpleProperty")]
        [TestCase("SimpleAutoProperty")]
        [TestCase("SimpleStaticProperty")]
        [TestCase("Indexer")]
        [TestCase("IndexerOverloads")]
        [TestCase("PropertyAccessors")]
        public void TestProperties(string testName)
        {
            AssertResourceTest(new CecilifyTestOptions
            {
                ResourceName = $"Members/Properties/{testName}", 
                IgnoredILErrors = null
            });
        }
    }
}
