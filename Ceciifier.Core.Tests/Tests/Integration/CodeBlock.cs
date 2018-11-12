using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Integration
{
	[TestFixture]
	public class BlockTestCase : IntegrationResourceBasedTest
	{
		[Test, Ignore("Not Implemented yet")]
		public void NonVirtualMethodCallTest()
		{
			AssertResourceTest(@"CodeBlock/MethodCall/NonVirtualMethodCall");
		}

        [Test]
		public void IfThenElseStatementTest()
		{
		    AssertResourceTestWithExplictExpectation(@"CodeBlock/Conditional/IfThenElseStatement", "System.Void IfThenElseStatement::Foo(System.Int32)");
        }

		[Test]
		public void IfStatementTest()
		{
			AssertResourceTestWithExplictExpectation(@"CodeBlock/Conditional/IfStatement", "System.Void IfStatement::Foo(System.Int32)");
		}

		[Test]
		public void NestedIfStatementTest()
		{
			AssertResourceTestWithExplictExpectation(@"CodeBlock/Conditional/NestedIfStatement", "System.Void NestedIfStatement::Foo(System.Int32)");
		}

		[Test, Ignore("Not Implemented yet")]
		public void SwitchStatementTest()
		{
			AssertResourceTest(@"CodeBlock/Conditional/");
		}

		[Test, Ignore("Not Implemented yet")]
		public void NullCoalescingTest()
		{
			AssertResourceTest(@"CodeBlock/Conditional/");
		}

	}
}
