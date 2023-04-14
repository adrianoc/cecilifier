using Cecilifier.Core.Tests.Framework;
using Cecilifier.Core.Tests.Integration;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    [TestFixture]
    public class TryCatchFinallyTestCase : ResourceTestBase
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
