using Ceciifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Ceciifier.Core.Tests
{
	[TestFixture]
	public class MiscTestCase : ResourceTestBase
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
		
        [Test, Ignore("Not Implemented yet")]
		public void TestExpressions()
		{
            AssertResourceTest(@"Misc\Expressions");
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
