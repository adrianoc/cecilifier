using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration.Types
{
    [TestFixture(typeof(MonoCecilContext))]
    public class EnumsTestCase<TResource> : ResourceTestBase<TResource> where TResource : IVisitorContext
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
