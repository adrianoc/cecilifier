using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture]
    public class MiscTestCase : IntegrationResourceBasedTest
    {
        [TestCase("Parameters")]
        [TestCase("Parameters2")]
        [TestCase("LocalVariables")]
        [TestCase("OnFields", Ignore = "Generates invalid code. See https://github.com/adrianoc/cecilifier/issues/61")]
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
        
        [TestCase("FunctionPointers", TestName = "Basic Tests")]
        [TestCase("VoidFunctionPointers")]
        [TestCase("FunctionPointersAsParameters")]
        [TestCase("GenericFunctionPointers")]
        public void TestFunctionPointer(string testName)
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

        [Test]
        public void TestDllImport()
        {
            AssertResourceTest($@"Misc/Attributes/DllImportAttribute");
        }
        
        [TestCase("TopLevelStatementsExplicitReturn")]
        [TestCase("TopLevelStatements")]
        public void TestTopLevelStatements(string testName)
        {
            AssertResourceTest($"Misc/{testName}", true);
        }
    }
}
