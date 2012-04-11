using Ceciifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Ceciifier.Core.Tests
{
    [TestFixture]
    public class MembersTestCase : ResourceTestBase
    {
        [Test]
        public void TestForwardReferences()
        {
            AssertResourceTest(@"Members\ForwardReferences");
        }
    }
}
