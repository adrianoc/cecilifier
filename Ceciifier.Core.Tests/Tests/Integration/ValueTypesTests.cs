using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Integration
{
	[TestFixture]
	public class ValueTypesTests : IntegrationResourceBasedTest
	{
		[TestCase("ValueTypeReturnAsTargetOfCall")]
		[TestCase("MultipleLiteralAsTargetOfCall")]
		[TestCase("SingleLiteralAsTargetOfCall")]
		[TestCase("ValueTypeReturnAsTargetOfCallInsideBaseContructorInvocation")]
		[TestCase("ValueTypeReturnAsTargetOfCallInsideContructor")]
		public void ValueTypeAsTargetOfCall(string testResourceBaseName)
		{
			AssertResourceTest($@"ValueTypes\AsTargetOfCall\{testResourceBaseName}");
		}
	}
}
