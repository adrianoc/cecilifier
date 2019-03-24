using System;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
	public class ExpressionTestCase : IntegrationResourceBasedTest
	{
		[Test]
		public void TestParameterAssignment()
		{
			AssertResourceTest(@"Expressions/ParameterAssignment");
		}

		[Test]
		public void TestLocalVariableAssignment()
		{
			AssertResourceTest(@"Expressions/LocalVariableAssignment");
		}

		[Test]
		public void TestMultipleLocalVariableAssignment()
		{
			AssertResourceTestWithExplictExpectation(@"Expressions/MultipleLocalVariableAssignment", "System.Void MultipleLocalVariableAssignment::Method(System.Int32,System.String)");
		}
		
		[Test]
		public void TestLocalVariableInitialization()
		{
			AssertResourceTest(@"Expressions/LocalVariableInitialization");
		}

		[Test]
		public void TestBox()
		{
			AssertResourceTest(@"Expressions/Box");
		}
		
		[Test]
		public void TestAdd()
		{
			AssertResourceTest(@"Expressions/Operators/Add");
		}

		[Test]
		public void TestAdd2()
		{
			AssertResourceTest(@"Expressions/Operators/Add2");
		}

		[Test]
		public void TestEquals()
		{
			AssertResourceTest(@"Expressions/Operators/Equals");
		}

	    [Test]
		public void TestLessThan()
		{
			AssertResourceTest(@"Expressions/Operators/LessThan");
		}

		[Test]
		public void TestTernaryOperator()
		{
			AssertResourceTestBinary(@"Expressions/Operators/Ternary", TestKind.Integration);
		}

		[Test]
		public void TestTypeInferenceInDeclarations()
		{
			AssertResourceTestWithExplictExpectation(@"Expressions/TypeInferenceInDeclarations", "System.Void TypeInferenceInDeclarations::Foo()");
		}

		[Test, Ignore("REQUIRES TRANSFORMATION")]
		public void TestValueTypeAddress()
		{
			AssertResourceTest(@"Expressions/ValueTypeAddress");
		}

		[Test, Ignore("newing primitives are not supported.")]
		public void TestNewPrimitive()
		{
			AssertResourceTest(@"Expressions/NewPrimitive");
		}

		[Test]
		public void TestNewCustom()
		{
			AssertResourceTest(@"Expressions/NewCustom");
		}
		
		[Test, Ignore("Not implemented yet")]
		public void TestNewArray()
		{
			AssertResourceTest(@"Expressions/NewArray");
		}
	}
}
