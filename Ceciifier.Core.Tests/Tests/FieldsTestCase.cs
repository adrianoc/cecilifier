using Ceciifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Ceciifier.Core.Tests
{
	[TestFixture]
	public class FieldsTestCase : ResourceTestBase
	{
		[Test]
		public void TestSingleField()
		{
			AssertResourceTest(@"Fields\SingleField");
		}

		[Test]
		public void TestSingleFieldMultipleModifiers()
		{
			AssertResourceTest(@"Fields\SingleFieldMultipleModifiers");
		}

		[Test]
		public void TestSimpleFieldsInSingleDeclaration()
		{
			AssertResourceTest(@"Fields\SimpleFieldsInSingleDeclaration");
		}

		[Test]
		public void TestVolatileField()
		{
			AssertResourceTest(@"Fields\VolatileField");
		}

		[Test]
		public void TestSingleRefField()
		{
			AssertResourceTest(@"Fields\SingleRefField");
		}

		[Test, Ignore("Not Implemented yet")]
		public void TestInitializedField()
		{
			AssertResourceTest(@"Fields\InitializedField");
		}

		[Test]
		public void TestSimpleArray()
		{
			AssertResourceTest(@"Fields\SimpleArray");
		}

		[Test, Ignore("Not Implemented yet")]
		public void TestJaggedArray()
		{
			AssertResourceTest(@"Fields\JaggedArray");
		}
	}
}
