using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration.Types
{
    [TestFixture]
    internal class ArraySmokeTests : ResourceTestBase
    {
        [Test]
        public void Test()
        {
            AssertResourceTest(@"Types/ArraySmoke");
        }
    }
}
