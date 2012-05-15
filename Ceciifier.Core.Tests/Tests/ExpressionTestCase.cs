using Ceciifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Ceciifier.Core.Tests
{
	[TestFixture]
	public class ExpressionTestCase : ResourceTestBase
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
		public void TestSimpeBox()
		{
			AssertResourceTest(@"Expressions\Box");
		}
		
		[Test]
		public void TestAdd()
		{
			AssertResourceTest(@"Expressions\Operators\Add");
		}

		[Test]
		public void TernaryOperatorTest()
		{
			AssertResourceTestBinary(@"Expressions\Operators\Ternary");
		}

		[Test]
		public void TypeInferenceInDeclarationsTest()
		{
			AssertResourceTest(@"Expressions\TypeInferenceInDeclarations");
		}
	}
}
