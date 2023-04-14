using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture]
    public class ValueTypesTests : ResourceTestBase
    {
        [TestCase("ValueTypeReturnAsTargetOfCall")]
        [TestCase("MultipleLiteralAsTargetOfCall")]
        [TestCase("SingleLiteralAsTargetOfCall")]
        [TestCase("SingleDoubleLiteralAsTargetOfCall")]
        [TestCase("SingleBoolLiteralAsTargetOfCall")]
        [TestCase("ValueTypeReturnAsTargetOfCallInsideBaseConstructorInvocation")]
        [TestCase("ValueTypeReturnAsTargetOfCallInsideConstructor")]
        public void ValueTypeAsTargetOfCall(string testResourceBaseName)
        {
            AssertResourceTest(new ResourceTestOptions { ResourceName = $"ValueTypes/AsTargetOfCall/{testResourceBaseName}" });
        }

        [Test]
        public void TestValueTypeInstantiation()
        {
            AssertResourceTest("ValueTypes/ValueTypeInstantiation");
        }
    }
}
