using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
	[TestFixture]
	public class FieldsTestCase : IntegrationResourceBasedTest
	{
		[Test]
		public void TestSingleField()
		{
			AssertResourceTest(@"Fields/SingleField");
		}

		[Test]
		public void TestInternalFields()
		{
			AssertResourceTest(@"Fields/InternalFields");
		}

		[Test]
		public void TestSingleFieldMultipleModifiers()
		{
			AssertResourceTest(@"Fields/SingleFieldMultipleModifiers");
		}

		[Test]
		public void TestSimpleFieldsInSingleDeclaration()
		{
			AssertResourceTest(@"Fields/SimpleFieldsInSingleDeclaration");
		}

		[Test]
		public void TestVolatileField()
		{
			AssertResourceTest(@"Fields/VolatileField");
		}

		[Test]
		public void TestSingleRefField()
		{
			AssertResourceTest(@"Fields/SingleRefField");
		}

		[Test]
		public void TestAssignment()
		{
			AssertResourceTest(@"Fields/Assignment");	
		}

		[Test, Ignore("Not Implemented yet")]
        public void TestInitializedFieldNoCtor()
		{
            AssertResourceTest(@"Fields/InitializedFieldNoCtor");
		}
		
        [Test, Ignore("Not Implemented yet")]
		public void TestInitializedFieldSingleCtor()
		{
            AssertResourceTest(@"Fields/InitializedFieldSingleCtor");
		}
		
        [Test, Ignore("Not Implemented yet")]
		public void TestInitializedFieldMultipleCtor()
		{
			AssertResourceTest(@"Fields/InitializedFieldMultipleCtor");
		}
        
        [Test, Ignore("Not Implemented yet")]
        public void TestInitializedFieldWithBaseCtor()
		{
            AssertResourceTest(@"Fields/InitializedFieldWithBaseCtor");
		}

		[Test]
		public void TestSimpleArray()
		{
			AssertResourceTest(@"Fields/SimpleArray");
		}

		[Test, Ignore("Not Implemented yet")]
		public void TestJaggedArray()
		{
			AssertResourceTest(@"Fields/JaggedArray");
		}
	}
}
