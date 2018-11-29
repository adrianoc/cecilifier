using System;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Integration
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
			AssertResourceTestWithExplictExpectation(@"Expressions/LocalVariableAssignment", "System.Void LocalVariableAssignment::Method(System.Int32)");
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
			AssertResourceTestWithExplictExpectation(@"Expressions/Box", "System.Void Box::Method()");
		}
		
		[Test]
		public void TestAdd()
		{
			AssertResourceTestWithExplictExpectation(@"Expressions/Operators/Add", "System.Void AddOperations::Integers(System.Int32)");
		}

		[Test]
		public void TestAdd2()
		{
			AssertResourceTestWithExplictExpectation(@"Expressions/Operators/Add2", "System.Void AddOperations2::IntegerString(System.String,System.Int32)");
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
			AssertResourceTest(@"Expressions/TypeInferenceInDeclarations");
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
	}
}
