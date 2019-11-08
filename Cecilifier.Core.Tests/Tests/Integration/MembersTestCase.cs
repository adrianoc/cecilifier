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
        
        [TestCase("SimpleEvent")]
        [TestCase("StaticEvent")]
        [TestCase("GenericEventHandler")]
        public void TestEvents(string testName)
        {
            AssertResourceTest($@"Members/Events/{testName}");
        }
    }
}
