using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture]
    public class BlockTestCase : ResourceTestBase
    {
        [Test]
        public void IfStatementTest()
        {
            AssertResourceTestWithExplicitExpectation(@"CodeBlock/Conditional/IfStatement", "System.Void IfStatement::Foo(System.Int32)");
        }

        [Test]
        public void IfThenElseStatementTest()
        {
            AssertResourceTestWithExplicitExpectation(@"CodeBlock/Conditional/IfThenElseStatement", "System.Void IfThenElseStatement::Foo(System.Int32)");
        }

        [Test]
        public void NestedIfStatementTest()
        {
            AssertResourceTestWithExplicitExpectation(@"CodeBlock/Conditional/NestedIfStatement", "System.Void NestedIfStatement::Foo(System.Int32)");
        }

        [Test]
        [Ignore("Not Implemented yet")]
        public void SwitchStatementTest()
        {
            AssertResourceTest(@"CodeBlock/Conditional/");
        }
    }
}
