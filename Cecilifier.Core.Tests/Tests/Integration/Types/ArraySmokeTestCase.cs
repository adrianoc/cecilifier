using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration.Types
{
    [TestFixture]
    internal class ArraySmokeTests : IntegrationResourceBasedTest
    {
        [Test]
        public void Test()
        {
            AssertResourceTest(@"Types/ArraySmoke");
        }
    }
}
