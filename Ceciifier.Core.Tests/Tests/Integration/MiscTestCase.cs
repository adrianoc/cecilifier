using Ceciifier.Core.Tests.Tests.Integration;
using NUnit.Framework;

namespace Ceciifier.Core.Tests
{
	[TestFixture]
	public class MiscTestCase : IntegrationResourceBasedTest
	{
		[Test, Ignore("Not Implemented yet")]
		public void TestDelegateInvocation()
		{
			AssertResourceTest(@"Misc\DelegateInvocation");
		}

		[Test, Ignore("Not Implemented yet")]
		public void TestTryCatchFinally()
		{
			AssertResourceTest(@"Misc\TryCatchFinally");
		}
		
		[Test]
		public void TestNamespaces()
		{
			AssertResourceTest(@"Misc\Namespaces");
		}

        [Test]
        public void TestAccessibilityModifiers()
        {
            AssertResourceTest(@"Misc\AccessibilityModifiers");
        }
	}
}
