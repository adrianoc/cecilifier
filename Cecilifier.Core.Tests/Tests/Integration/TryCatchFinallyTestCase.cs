using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture(typeof(MonoCecilContext))]
    public class TryCatchFinallyTestCase<TResource> : ResourceTestBase<TResource> where TResource : IVisitorContext
    {
        [TestCase("TryCatch")]
        [TestCase("TryFinally")]
        [TestCase("TryCatchFinally", true)]
        [TestCase("NestedTryCatchFinally", true)]
        [TestCase("TryMultipleCatches")]
        [TestCase("NestedTryCatch")]
        public void TestExceptionHandlers(string testName, bool compareWithExplicitIL = false)
        {
            if (compareWithExplicitIL)
            {
                AssertResourceTestWithExplicitExpectation($"TryCatchFinally/{testName}", $"System.Void {testName}::Foo(System.Int32)");
            }
            else
            {
                AssertResourceTest($"TryCatchFinally/{testName}");
            }
        }
    }
}
