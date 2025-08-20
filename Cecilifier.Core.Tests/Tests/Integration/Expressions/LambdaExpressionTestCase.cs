using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture(typeof(MonoCecilContext))]
    public class LambdaExpressionTestCase<TResource> : ResourceTestBase<TResource> where TResource : IVisitorContext
    {
        [Test]
        public void NonCapturingLambda_VariableInitializer()
        {
            AssertResourceTestWithExplicitExpectation(@"Expressions/Lambda/VariableInitializer", "System.Void C::VariableInitializer()");
        }

        [Test]
        public void NonCapturingLambda_VariableAssignment()
        {
            AssertResourceTestWithExplicitExpectation(@"Expressions/Lambda/VariableAssignment", "System.Void C::VariableAssignment()");
        }

        [Test]
        public void NonCapturingLambda_Parameter()
        {
            AssertResourceTestWithExplicitExpectation(@"Expressions/Lambda/Parameter", "System.Void C::Parameter(System.Func`2<System.Int32,System.Int32>)");
        }

        [Test]
        public void NonCapturingLambda_SimpleLambdaExpression()
        {
            AssertResourceTestWithExplicitExpectation(@"Expressions/Lambda/SimpleLambdaExpression", "System.Void C::SimpleLambdaExpression()");
        }

        [Test]
        public void NonCapturingLambda_MappedToAction()
        {
            AssertResourceTestWithExplicitExpectation(@"Expressions/Lambda/MappedToAction", "System.Void C::MappedToAction(System.Action`2<System.Int32,System.Int32>)");
        }
    }
}
