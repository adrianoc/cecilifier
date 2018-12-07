using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
	[TestFixture]
	public class MiscTestCase : IntegrationResourceBasedTest
	{
		[TestCase("Parameters")]
		[TestCase("LocalVariables")]
		public void TestDelegateInvocation(string storageType)
		{
			AssertResourceTest($@"Misc/DelegateInvocation_{storageType}");
		}

		[Test, Ignore("Not Implemented yet")]
		public void TestTryCatchFinally()
		{
			AssertResourceTest(@"Misc/TryCatchFinally");
		}
		
		[Test]
		public void TestNamespaces()
		{
			AssertResourceTest(@"Misc/Namespaces");
		}

        [Test]
        public void TestAccessibilityModifiers()
        {
            AssertResourceTest(@"Misc/AccessibilityModifiers");
        }
	}
}
