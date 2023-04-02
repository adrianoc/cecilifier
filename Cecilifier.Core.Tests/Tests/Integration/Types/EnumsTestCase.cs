using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration.Types
{
    [TestFixture]
    public class EnumsTestCase : ResourceTestBase
    {
        [TestCase("SimpleEnum")]
        [TestCase("SimpleEnumWithNonDefaultValues")]
        [TestCase("SelfReferencingEnum")]
        [TestCase("SelfReferencingNonContinuousEnum")]
        [TestCase("AllExpressionsInInitializerEnum")]
        [TestCase("EnumFlags")]
        public void SimplestTest(string testCaseName)
        {
            AssertResourceTest($@"Types/Enums/{testCaseName}");
        }
    }
}
