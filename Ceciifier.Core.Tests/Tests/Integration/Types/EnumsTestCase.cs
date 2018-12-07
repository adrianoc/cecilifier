using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration.Types
{
    [TestFixture]
    public class EnumsTestCase : IntegrationResourceBasedTest
    {
        [TestCase("SimpleEnum")]
        [TestCase("SimpleEnumWithNonDefaultValues")]
        public void SimplestTest(string testCaseName)
        {
            AssertResourceTest($@"Types/Enums/{testCaseName}");
        }
    }
}