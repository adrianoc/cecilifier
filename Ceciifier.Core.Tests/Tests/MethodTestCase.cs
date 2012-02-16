using Ceciifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Ceciifier.Core.Tests
{
	[TestFixture]
	public class MethodTestCase : ResourceTestBase
	{
		[Test, Ignore("Not Implemented yet")]
		public void TestExplicityDefaultCtor()
		{
			AssertResourceTest(@"Methods\ExplicityDefaultCtor");
		}
		
		[Test, Ignore("Not Implemented yet")]
		public void TestMultipleParameters()
		{
			AssertResourceTest(@"Methods\MultipleParameters");
		}

		[Test, Ignore("Not Implemented yet")]
		public void TestNoParameters()
		{
			AssertResourceTest(@"Methods\NoParameters");
		}

		[Test, Ignore("Not Implemented yet")]
		public void TestSingleSimpleParameter()
		{
			AssertResourceTest(@"Methods\SingleSimpleParameter");
		}

		[Test, Ignore("Not Implemented yet")]
		public void TestVariableParameterCount()
		{
			AssertResourceTest(@"Methods\VariableParameterCount");
		}
		
		[Test, Ignore("Not Implemented yet")]
		public void TestVirtualMethod()
		{
			AssertResourceTest(@"Methods\VirtualMethod");
		}

		[Test, Ignore("Not Implemented yet")]
		public void TestAbstractMethod()
		{
			AssertResourceTest(@"Methods\AbstractMethod");
		}
	}
}
