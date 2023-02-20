using Cecilifier.Core.Tests.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    public class IntegrationResourceBasedTest : ResourceTestBase
    {
        protected void AssertResourceTest(string resource)
        {
            AssertResourceTest(resource, TestKind.Integration);
        }
    }
}
