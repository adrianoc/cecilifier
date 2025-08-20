using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture(typeof(MonoCecilContext))]
    public class ValueTypesTests<TResource> : ResourceTestBase<TResource> where TResource : IVisitorContext
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
            AssertResourceTest(new CecilifyTestOptions { ResourceName = $"ValueTypes/AsTargetOfCall/{testResourceBaseName}" });
        }

        [Test]
        public void TestValueTypeInstantiation()
        {
            AssertResourceTest("ValueTypes/ValueTypeInstantiation");
        }
    }
}
