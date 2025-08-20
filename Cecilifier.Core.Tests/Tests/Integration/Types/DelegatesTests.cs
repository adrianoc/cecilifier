using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration.Types
{
    [TestFixture(typeof(MonoCecilContext))]
    public class DelegatesTests<TResource> : ResourceTestBase<TResource> where TResource : IVisitorContext
    {
        [TestCase("CustomDelegateMultipleParameters")]
        [TestCase("ParameterlessDelegates")]
        public void NumberOfParameters(string testName)
        {
            AssertResourceTest(@$"Types/Delegates/{testName}");
        }
    }
}
