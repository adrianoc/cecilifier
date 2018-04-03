using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Integration
{
	[TestFixture]
	class BlockTestCase : IntegrationResourceBasedTest
	{
		[Test, Ignore("Not Implemented yet")]
		public void NonVirtualMethodCallTest()
		{
			AssertResourceTest(@"CodeBlock\MethodCall\NonVirtualMethodCall");
		}

        [Test]
		public void IfThenElseStatementTest()
		{
			AssertResourceTest(@"CodeBlock\Conditional\IfThenElseStatement");
		}

		[Test]
		public void IfStatementTest()
		{
			AssertResourceTest(@"CodeBlock\Conditional\IfStatement");
		}

		[Test, Ignore("Not Implemented yet")]
		public void SwitchStatementTest()
		{
			AssertResourceTest(@"CodeBlock\Conditional\");
		}

		[Test, Ignore("Not Implemented yet")]
		public void NullCoalescingTest()
		{
			AssertResourceTest(@"CodeBlock\Conditional\");
		}

	}
}
