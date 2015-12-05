using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Integration
{
	[TestFixture]
	public class ExpressionTestCase : IntegrationResourceBasedTest
	{
		[Test]
		public void TestParameterAssignment()
		{
			AssertResourceTest(@"Expressions\ParameterAssignment");
		}

		[Test]
		public void TestLocalVariableAssignment()
		{
			AssertResourceTest(@"Expressions\LocalVariableAssignment");
		}

		[Test]
		public void TestMultipleLocalVariableAssignment()
		{
			AssertResourceTest(@"Expressions\MultipleLocalVariableAssignment");
		}
		
		[Test]
		public void TestLocalVariableInitialization()
		{
			AssertResourceTest(@"Expressions\LocalVariableInitialization");
		}

		[Test]
		public void TestBox()
		{
			AssertResourceTest(@"Expressions\Box");
		}
		
		[Test]
		public void TestAdd()
		{
			AssertResourceTest(@"Expressions\Operators\Add");
		}

		[Test]
		public void TestTernaryOperator()
		{
			AssertResourceTestBinary(@"Expressions\Operators\Ternary", TestKind.Integration);
		}

		[Test]
		public void TestTypeInferenceInDeclarations()
		{
			AssertResourceTest(@"Expressions\TypeInferenceInDeclarations");
		}

		[Test, Ignore("REQUIRES TRANSFORMATION")]
		public void TestValueTypeAddress()
		{
			AssertResourceTest(@"Expressions\ValueTypeAddress");
		}

		[Test, Ignore("newing primitives are not supported.")]
		public void TestNewPrimitive()
		{
			AssertResourceTest(@"Expressions\NewPrimitive");
		}

		[Test]
		public void TestNewCustom()
		{
			AssertResourceTest(@"Expressions\NewCustom");
		}
	}
}
