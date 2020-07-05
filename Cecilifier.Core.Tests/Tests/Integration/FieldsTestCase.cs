using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture]
    public class FieldsTestCase : IntegrationResourceBasedTest
    {
        [Test]
        public void TestAssignment()
        {
            AssertResourceTest(@"Members/Fields/Assignment");
        }

        [Test]
        [Ignore("Not Implemented yet")]
        public void TestInitializedFieldMultipleCtor()
        {
            AssertResourceTest(@"Members/Fields/InitializedFieldMultipleCtor");
        }

        [Test]
        [Ignore("Not Implemented yet")]
        public void TestInitializedFieldNoCtor()
        {
            AssertResourceTest(@"Members/Fields/InitializedFieldNoCtor");
        }

        [Test]
        [Ignore("Not Implemented yet")]
        public void TestInitializedFieldSingleCtor()
        {
            AssertResourceTest(@"Members/Fields/InitializedFieldSingleCtor");
        }

        [Test]
        [Ignore("Not Implemented yet")]
        public void TestInitializedFieldWithBaseCtor()
        {
            AssertResourceTest(@"Members/Fields/InitializedFieldWithBaseCtor");
        }

        [Test]
        public void TestInternalFields()
        {
            AssertResourceTest(@"Members/Fields/InternalFields");
        }

        [Test]
        [Ignore("Not Implemented yet")]
        public void TestJaggedArray()
        {
            AssertResourceTest(@"Members/Fields/JaggedArray");
        }

        [Test]
        public void TestSimpleArray()
        {
            AssertResourceTest(@"Members/Fields/SimpleArray");
        }

        [Test]
        public void TestSimpleFieldsInSingleDeclaration()
        {
            AssertResourceTest(@"Members/Fields/SimpleFieldsInSingleDeclaration");
        }

        [Test]
        public void TestSingleField()
        {
            AssertResourceTest(@"Members/Fields/SingleField");
        }

        [Test]
        public void TestSingleFieldMultipleModifiers()
        {
            AssertResourceTest(@"Members/Fields/SingleFieldMultipleModifiers");
        }

        [Test]
        public void TestSingleRefField()
        {
            AssertResourceTest(@"Members/Fields/SingleRefField");
        }

        [Test]
        public void TestVolatileField()
        {
            AssertResourceTest(@"Members/Fields/VolatileField");
        }
    }
}
