using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture(typeof(MonoCecilContext))]
    public class MembersTestCase<TResource> : ResourceTestBase<TResource> where TResource : IVisitorContext
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
            AssertResourceTest($"Members/Events/{testName}");
        }
    }
}
