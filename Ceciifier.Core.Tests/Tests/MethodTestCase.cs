using Ceciifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Ceciifier.Core.Tests
{
	[TestFixture]
	public class MethodTestCase : ResourceTestBase
	{
		[Test]
		public void TestExplicityDefaultCtor()
		{
			AssertResourceTest(@"Methods\ExplicityDefaultCtor");
		}
		
		[Test]
		public void TestMultipleParameters()
		{
			AssertResourceTest(@"Methods\MultipleParameters");
		}

		[Test]
		public void TestNoParameters()
		{
			AssertResourceTest(@"Methods\NoParameters");
		}

		[Test]
		public void TestSingleSimpleParameter()
		{
			AssertResourceTest(@"Methods\SingleSimpleParameter");
		}

		[Test, Ignore("Not Implemented yet")]
		public void TestVariableNumberOfParameters()
		{
			AssertResourceTest(@"Methods\VariableNumberOfParameters");
		}
		
		[Test, Ignore("Not Implemented yet")]
		public void TestVirtualMethod()
		{
			AssertResourceTest(@"Methods\VirtualMethod");
		}

		[Test]
		public void TestAbstractMethod()
		{
			AssertResourceTest(@"Methods\AbstractMethod");
		}

		[Test]
		public void TestReturnValue()
		{
			AssertResourceTest(@"Methods\ReturnValue");
		}

		[Test, Ignore("Not Implemented yet")]
		public void TestInOutRefParameters()
		{
			AssertResourceTest(@"Methods\InOutRefParameters");
		}

		[Test]
		public void TestDefaultCtorFromBaseClass()
		{
			AssertResourceTest(@"Methods\DefaultCtorFromBaseClass");
		}

		[Test]
		public void TestInterfaceMethodVirtualInInplementation()
		{
			AssertResourceTest(@"Methods\InterfaceMethodVirtualInInplementation");
		}

		[Test]
		public void TestCtorWithParameters()
		{
			AssertResourceTest(@"Methods\CtorWithParameters");
		}

		[Test]
		public void TestMutuallyRecursive()
		{
			AssertResourceTest(@"Methods\MutuallyRecursive");
		}

		[Test]
		public void TestMethodCallOnValueType()
		{
			AssertResourceTest(@"Methods\MethodCallOnValueType");
		}
		
		[Test]
		public void TestExternalMethodReference()
		{
			AssertResourceTest(@"Methods\ExternalMethodReference");
		}
	}
}
