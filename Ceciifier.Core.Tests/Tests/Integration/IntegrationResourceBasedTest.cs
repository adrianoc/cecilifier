using Ceciifier.Core.Tests.Framework;

namespace Ceciifier.Core.Tests.Tests.Integration
{
	public class IntegrationResourceBasedTest : ResourceTestBase
	{
		protected void AssertResourceTest(string resource)
		{
			AssertResourceTest(resource, TestKind.Integration);
		}
	}
}
