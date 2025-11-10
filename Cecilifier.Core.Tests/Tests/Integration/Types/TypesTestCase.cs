using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using Cecilifier.Core.Tests.Framework.Attributes;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration.Types
{
    [TestFixture(typeof(MonoCecilContext), TestName = "Mono.Cecil")]
    [TestFixture(typeof(SystemReflectionMetadataContext), TestName = "SRM")]
    [EnableForContext<SystemReflectionMetadataContext>(nameof(SimplestTest), nameof(SealedClassTest), nameof(AbstractClassTest), IgnoreReason = "Not implemented")]
    public class TypesTestCase<TContext> : ResourceTestBase<TContext> where TContext : IVisitorContext
    {
        [TestCase("SimpleTypeWithAttribute")]
        [TestCase("AttributeWithProperty")]
        [TestCase("AttributeFromSameAssembly")]
        [TestCase("AttributeWithTypeOfExpression")]
        [TestCase("AttributeGeneric")]
        public void AttributeTests(string typeName)
        {
            AssertResourceTest($"Types/{typeName}");
        }

        [Test]
        public void AbstractClassTest()
        {
            AssertResourceTest("Types/AbstractClass");
        }

        [Test]
        public void ExplicitInterfaceImplementationTest()
        {
            AssertResourceTest("Types/ExplicitInterfaceImplementation");
        }

        [Test]
        public void ForwardTypeReferenceTest()
        {
            AssertResourceTest("Types/ForwardTypeReference");
        }

        [Test]
        public void InheritanceSameCompilationUnitTest()
        {
            AssertResourceTest("Types/InheritanceSameCompilationUnit");
        }

        [Test]
        public void InheritanceTest()
        {
            AssertResourceTest("Types/Inheritance");
        }

        [Test]
        public void InnerClassTest()
        {
            AssertResourceTest("Types/InnerClass");
        }

        [Test]
        public void InterfaceDefinitionTest()
        {
            AssertResourceTest("Types/InterfaceDefinition");
        }

        [Test]
        public void InterfaceWithPropertiesTest()
        {
            AssertResourceTest("Types/InterfaceWithProperties");
        }

        [Test]
        public void InterfaceImplementationTest()
        {
            AssertResourceTest("Types/InterfaceImplementation");
        }

        [Test]
        public void MultipleInterfaceImplementationTest()
        {
            AssertResourceTest("Types/MultipleInterfaceImplementation");
        }

        [Test]
        public void PartialClassTest()
        {
            AssertResourceTest("Types/PartialClass");
        }

        [Test]
        public void SealedClassTest()
        {
            AssertResourceTest("Types/SealedClass");
        }

        [Test]
        public void SimplestTest()
        {
            AssertResourceTest("Types/Simplest");
        }

        [Test]
        public void SimpleValueTypeTest()
        {
            AssertResourceTest("Types/SimpleValueType");
        }

        [Test]
        public void TypeInitializeTest()
        {
            AssertResourceTest("Types/TypeInitializer");
        }

        [Test]
        public void ReadOnlyStructTest()
        {
            AssertResourceTest("Types/ReadOnlyStruct");
        }
    }
}
