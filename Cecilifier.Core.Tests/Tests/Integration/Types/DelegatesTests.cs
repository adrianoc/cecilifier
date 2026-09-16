using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using Cecilifier.Core.Tests.Framework.Attributes;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration.Types
{
    [TestFixture(typeof(MonoCecilContext))]
    [TestFixture(typeof(SystemReflectionMetadataContext))]
    [EnableForContext<SystemReflectionMetadataContext>(IgnoreReason = "Not Supported")]
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
