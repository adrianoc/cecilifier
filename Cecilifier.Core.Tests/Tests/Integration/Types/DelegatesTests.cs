using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration.Types
{
    public class DelegatesTests : ResourceTestBase
    {
        [TestCase("CustomDelegateMultipleParameters")]
        [TestCase("ParameterlessDelegates")]
        public void NumberOfParameters(string testName)
        {
            AssertResourceTest(@$"Types/Delegates/{testName}");
        }
    }
}
