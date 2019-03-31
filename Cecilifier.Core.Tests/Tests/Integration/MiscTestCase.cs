using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture]
    public class MiscTestCase : IntegrationResourceBasedTest
    {
        [TestCase("Parameters")]
        [TestCase("Parameters2")]
        [TestCase("LocalVariables")]
        public void TestDelegateInvocation(string storageType)
        {
            AssertResourceTest($@"Misc/DelegateInvocation_{storageType}");
        }

        [Test]
        public void TestAccessibilityModifiers()
        {
            AssertResourceTest(@"Misc/AccessibilityModifiers");
        }

        [Test]
        public void TestNamespaces()
        {
            AssertResourceTest(@"Misc/Namespaces");
        }
    }
}
