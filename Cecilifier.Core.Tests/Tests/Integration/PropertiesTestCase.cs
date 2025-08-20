using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture(typeof(MonoCecilContext))]
    public class PropertiesTestCase<TResource> : ResourceTestBase<TResource> where TResource : IVisitorContext
    {
        [TestCase("SimpleProperty")]
        [TestCase("SimpleAutoProperty")]
        [TestCase("SimpleStaticProperty")]
        [TestCase("Indexer")]
        [TestCase("IndexerOverloads")]
        [TestCase("PropertyAccessors", "ClassLoadGeneral")] //https://github.com/adrianoc/cecilifier/issues/227
        public void TestProperties(string testName, string ignoredILErrors = null)
        {
            AssertResourceTest(new CecilifyTestOptions
            {
                ResourceName = $"Members/Properties/{testName}", 
                IgnoredILErrors = ignoredILErrors
            });
        }
    }
}
