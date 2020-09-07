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

        [TestCase("AddressOfParameters", TestName = "Parameters")]
        [TestCase("AddressOfLocalVariables", TestName = "Local Variables")]
        [TestCase("AssignmentOfAddressOfLocalVariables", TestName = "Local Variable Assignment")]
        public void TestPointerTypes(string testName)
        {
            AssertResourceTest($@"Misc/Pointers/{testName}");       
        }
        
        [TestCase("Delegate")]
        [TestCase("ClassAndMembers")]
        [TestCase("InterfaceAndMembers")]
        [TestCase("EnumAndMembers")]
        [TestCase("StructAndMembers")]
        public void AttributesOnMembers(string typeKind)
        {
            AssertResourceTest($@"Misc/Attributes/AttributesOn{typeKind}");
        }

        [Test]
        public void TestAttributesOnExplicitTargets()
        {
            AssertResourceTest($@"Misc/Attributes/AttributesOnExplicitTargets");
        }

        [Test]
        public void TestAttributeWithArrayInitializer()
        {
            AssertResourceTest($@"Misc/Attributes/AttributeWithArrayInitializer");
        }
    }
}
