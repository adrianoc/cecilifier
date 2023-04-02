using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    public class ExpressionTestCase : ResourceTestBase
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
            AssertResourceTestWithExplicitExpectation(@"Expressions/MultipleLocalVariableAssignment", "System.Void MultipleLocalVariableAssignment::Method(System.Int32,System.String)");
        }

        [Test]
        public void TestLocalVariableInitialization()
        {
            AssertResourceTest(@"Expressions/LocalVariableInitialization");
        }

        [Test]
        public void TestDoubleLocalVariableInitialization()
        {
            AssertResourceTest(@"Expressions/DoubleLocalVariableInitialization");
        }

        [Test]
        public void TestDoubleLocalVariableInitializationComplex()
        {
            AssertResourceTestWithExplicitExpectation(@"Expressions/DoubleLocalVariableInitializationComplex", "System.Double DoubleLocalVariableInitializationComplex::Complex(System.Int32,System.Double)");
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
            AssertResourceTestWithExplicitExpectation(@"Expressions/Operators/Add2", "System.Void AddOperations2::IntegerString(System.String,System.Int32)");
        }

        [Test]
        public void TestTimes()
        {
            AssertResourceTest(@"Expressions/Operators/Times");
        }

        [Test]
        public void TestModulus()
        {
            AssertResourceTest(@"Expressions/Operators/Arithmetic/Modulus");
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
            AssertResourceTestBinary(@"Expressions/Operators/Ternary");
        }

        [Test]
        public void TestTypeInferenceInDeclarations()
        {
            AssertResourceTestWithExplicitExpectation(@"Expressions/TypeInferenceInDeclarations", "System.Void TypeInferenceInDeclarations::Foo()");
        }

        [Test]
        public void TestValueTypeAddress()
        {
            AssertResourceTest(@"Expressions/ValueTypeAddress");
        }

        [Test]
        public void TestNewPrimitive()
        {
            AssertResourceTest(@"Expressions/NewPrimitive");
        }

        [Test]
        public void TestNewCustom()
        {
            AssertResourceTest(@"Expressions/NewCustom");
        }

        [Test]
        public void TestNewSingleDimensionArray()
        {
            AssertResourceTest(@"Expressions/NewSingleDimensionArray");
        }

        [TestCase("Minus")]
        [TestCase("Not")]
        [TestCase("NotBinary")]
        public void TestUnaryExpressions(string testName)
        {
            AssertResourceTest($@"Expressions/Operators/Unary/{testName}");
        }

        [Test]
        public void TestIncrementDecrementExpressions([Values("Pre", "Post")] string kind, [Values("Increment", "Decrement")] string expressionType, [Values("Param", "Field", "Local", "Prop")] string memberType)
        {
            var testName = $"{kind}{expressionType}{memberType}";
            AssertResourceTestWithExplicitExpectation($@"Expressions/Operators/Unary/{testName}", $"System.Int32 {testName}::M(System.Int32)");
        }

        [TestCase("ArrayRead")]
        [TestCase("PropertyRead")]
        [TestCase("ArrayWrite")]
        [TestCase("PropertyWrite")]
        public void TestIndexerAccess(string prefix)
        {
            AssertResourceTest($@"Expressions/{prefix}IndexerAccess");
        }

        [Test]
        public void TestArrayLength()
        {
            AssertResourceTest($@"Expressions/ArrayLength");
        }

        [Test]
        public void TestRangeExpression()
        {
            AssertResourceTest(@"Expressions/RangeExpression");
        }

        [Test]
        public void TestIndexExpression()
        {
            AssertResourceTestWithExplicitExpectation(@"Expressions/IndexExpression", "System.Int32 C::M(System.Int32,System.Int32[])");
        }

        [TestCase("Parameters")]
        [TestCase("LocalVariables")]
        [TestCase("Instance_Method")]
        [TestCase("Static_Method")]
        [TestCase("LocalVariablesInitializer")]
        public void TestDelegateAssignment(string memberType)
        {
            AssertResourceTest($"Expressions/DelegateAssignment_{memberType}");
        }

        [Test]
        public void TestDelegateInvocation()
        {
            AssertResourceTest("Expressions/DelegateInvocation");
        }

        [Test]
        public void TestExpressionBodiedMembers()
        {
            AssertResourceTest("Expressions/ExpressionBodiedMembers");
        }
    }
}
