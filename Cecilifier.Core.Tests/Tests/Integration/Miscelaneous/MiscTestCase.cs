using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture]
    public class MiscTestCase : ResourceTestBase
    {
        [TestCase("Parameters")]
        [TestCase("Parameters2")]
        [TestCase("LocalVariables")]
        [TestCase("OnFields", Ignore = "Generates invalid code. See https://github.com/adrianoc/cecilifier/issues/61")]
        public void TestDelegateInvocation(string storageType)
        {
            AssertResourceTest($"Misc/DelegateInvocation_{storageType}");
        }

        [Test]
        public void TestAccessibilityModifiers()
        {
            AssertResourceTest("Misc/AccessibilityModifiers");
        }

        [Test]
        public void TestNamespaces()
        {
            AssertResourceTest("Misc/Namespaces");
        }

        [TestCase("AddressOfParameters", TestName = "Parameters")]
        [TestCase("AddressOfLocalVariables", TestName = "Local Variables")]
        [TestCase("AssignmentOfAddressOfLocalVariables", TestName = "Local Variable Assignment")]
        public void TestPointerTypes(string testName)
        {
            //https://github.com/adrianoc/cecilifier/issues/227
            //My best bet is that this has something to do with missing permission sets.
            //When looking into compiler generated binaries the assemblies contains the following:
            /*
	         *   .permissionset reqmin = {
		     *       [System.Private.CoreLib]System.Security.Permissions.SecurityPermissionAttribute = {
			 *           property bool SkipVerification = bool(true)
		     *       }
	         *   }
             */
            // Don't know how to emit that using cecil yet. `AssemblyDefinition.HasSecurityDeclarations` and `AssemblyDefinition.SecurityDeclarations`
            // are likely involved. For now we'll prevent ilverification to fail the test since the code looks to run with no problem
            AssertResourceTest(new CecilifyTestOptions
            {
                ResourceName = $"Misc/Pointers/{testName}", 
                IgnoredILErrors = "ExpectedNumericType"
            });
        }

        [TestCase("FunctionPointers", TestName = "Basic Tests")]
        [TestCase("VoidFunctionPointers")]
        [TestCase("FunctionPointersAsParameters")]
        [TestCase("GenericFunctionPointers")]
        public void TestFunctionPointer(string testName)
        {
            AssertResourceTest(new CecilifyTestOptions
            {
                ResourceName = $"Misc/Pointers/{testName}", 
                FailOnAssemblyVerificationErrors = IgnoredKnownIssue.MiscILVerifyVailuresNeedsInvestigation // https://github.com/adrianoc/cecilifier/issues/227
            });
        }

        [TestCase("Delegate")]
        [TestCase("ClassAndMembers")]
        [TestCase("InterfaceAndMembers")]
        [TestCase("EnumAndMembers")]
        [TestCase("StructAndMembers")]
        public void AttributesOnMembers(string typeKind)
        {
            AssertResourceTest($"Misc/Attributes/AttributesOn{typeKind}");
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
            AssertResourceTest(new CecilifyTestOptions { ResourceName = $"Misc/{testName}", BuildType = BuildType.Exe });
        }
    }
}
