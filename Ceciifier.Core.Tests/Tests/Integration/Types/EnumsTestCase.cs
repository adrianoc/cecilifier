using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration.Types
{
    [TestFixture]
    public class EnumsTestCase : IntegrationResourceBasedTest
    {
        [TestCase("SimpleEnum")]
        [TestCase("SimpleEnumWithNonDefaultValues")]
        [TestCase("SelfReferencingEnum")]
        [TestCase("SelfReferencingNonContinuousEnum")]
        [TestCase("AllExpressionsInInitializerEnum")]
        [TestCase("EnumFlags", IgnoreReason = "Type Attributes not supported yet")]
        public void SimplestTest(string testCaseName)
        {
            AssertResourceTest($@"Types/Enums/{testCaseName}");
        }
    }
}