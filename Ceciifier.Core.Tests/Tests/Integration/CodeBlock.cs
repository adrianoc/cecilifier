using Ceciifier.Core.Tests.Tests.Integration;
using NUnit.Framework;

namespace Ceciifier.Core.Tests
{
	[TestFixture]
	class BlockTestCase : IntegrationResourceBasedTest
	{
		[Test, Ignore("Not Implemented yet")]
		public void NonVirtualMethodCallTest()
		{
			AssertResourceTest(@"CodeBlock\MethodCall\NonVirtualMethodCall");
		}

		[Test, Ignore("Not Implemented yet")]
		public void IfThenElseStatementTest()
		{
			AssertResourceTest(@"CodeBlock\Conditional\IfThenElseStatement");
		}

		[Test, Ignore("Not Implemented yet")]
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
