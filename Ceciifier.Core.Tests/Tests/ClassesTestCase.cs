using Ceciifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Ceciifier.Core.Tests
{
	[TestFixture]
	class ClassesTestCase : ResourceTestBase
	{
		[Test]
		public void SimplestTest()
		{
			AssertResourceTest(@"Classes\Simplest");
		}

		[Test]
		public void SealedClassTest()
		{
			AssertResourceTest(@"Classes\SealedClass");
		}

		[Test]
		public void AbstractClassTest()
		{
			AssertResourceTest(@"Classes\AbstractClass");
		}
		
		[Test]
		public void PartialClassTest()
		{
			AssertResourceTest(@"Classes\PartialClass");
		}

		[Test]
		public void InnerClassTest()
		{
			AssertResourceTest(@"Classes\InnerClass");
		}

		[Test]
		public void InterfaceImplementationTest()
		{
			AssertResourceTest(@"Classes\InterfaceImplementation");
		}
		
		[Test]
		public void ExplicityInterfaceImplementationTest()
		{
			AssertResourceTest(@"Classes\ExplicityInterfaceImplementation");
		}
		
		[Test]
		public void InheritanceTest()
		{
			AssertResourceTest(@"Classes\Inheritance");
		}

		[Test]
		public void MultipleInterfaceImplementationTest()
		{
			AssertResourceTest(@"Classes\MultipleInterfaceImplementation");
		}
	}
}
