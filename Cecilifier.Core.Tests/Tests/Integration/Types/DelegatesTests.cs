using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration.Types
{
    public class DelegatesTests : IntegrationResourceBasedTest
    {
        [TestCase("CustomDelegateMultipleParameters")]
        [TestCase("ParameterlessDelegates")]
        public void NumberOfParameters(string testName)
        {
            AssertResourceTest(@$"Types/Delegates/{testName}");
        }
    }
}
