using Ceciifier.Core.Tests.Tests.Integration;
using NUnit.Framework;

namespace Ceciifier.Core.Tests
{
    [TestFixture]
	public class MembersTestCase : IntegrationResourceBasedTest
    {
        [Test]
        public void TestForwardReferences()
        {
            AssertResourceTest(@"Members\ForwardReferences");
        }
    }
}
