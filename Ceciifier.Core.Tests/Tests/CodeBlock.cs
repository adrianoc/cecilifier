using Ceciifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Ceciifier.Core.Tests
{
	[TestFixture]
	class BlockTestCase : ResourceTestBase
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
		public void TernaryOperatorTest()
		{
			AssertResourceTest(@"CodeBlock\Conditional\");
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
