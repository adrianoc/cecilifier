using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture]
    public class MembersTestCase : ResourceTestBase
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
            AssertResourceTest(new ResourceTestOptions
            {
                ResourceName = $"Members/Events/{testName}",
                FailOnAssemblyVerificationErrors = false //https://github.com/adrianoc/cecilifier/issues/226
            });
        }
    }
}
