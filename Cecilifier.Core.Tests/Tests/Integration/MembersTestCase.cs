using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture]
    public class MembersTestCase : IntegrationResourceBasedTest
    {
        [Test]
        public void TestForwardReferences()
        {
            AssertResourceTest(@"Members/ForwardReferences");
        }
    }
}
