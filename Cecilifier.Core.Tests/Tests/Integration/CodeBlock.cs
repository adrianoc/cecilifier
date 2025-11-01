using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    // These tests have explicit IL expectation which differs from SRM so we only test Mono.Cecil.
    // 
    [TestFixture(typeof(MonoCecilContext))] 
    public class BlockTestCase<TResource> : ResourceTestBase<TResource> where TResource : IVisitorContext
    {
        [Test]
        public void IfStatementTest()
        {
            AssertResourceTestWithExplicitExpectation("CodeBlock/Conditional/IfStatement", "System.Void IfStatement::Foo(System.Int32)");
        }

        [Test]
        public void IfThenElseStatementTest()
        {
            AssertResourceTestWithExplicitExpectation("CodeBlock/Conditional/IfThenElseStatement", "System.Void IfThenElseStatement::Foo(System.Int32)");
        }

        [Test]
        public void NestedIfStatementTest()
        {
            AssertResourceTestWithExplicitExpectation("CodeBlock/Conditional/NestedIfStatement", "System.Void NestedIfStatement::Foo(System.Int32)");
        }

        [Test]
        [Ignore("Not Implemented yet")]
        public void SwitchStatementTest()
        {
            AssertResourceTest("CodeBlock/Conditional/");
        }
    }
}
