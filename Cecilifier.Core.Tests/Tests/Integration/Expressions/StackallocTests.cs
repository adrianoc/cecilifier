using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using Cecilifier.Core.Tests.Framework.Attributes;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration;

[TestFixture(typeof(MonoCecilContext))]
[TestFixture(typeof(SystemReflectionMetadataContext))]
public class StackallocTests<TResource> : ResourceTestBase<TResource> where TResource : IVisitorContext
{
    [TestCase("simplest", TestName = "Simplest")]
    [TestCase("WithSpan", TestName = "WithSpan")]
    [TestCase("WithSpanAsParameter", true, TestName = "WithSpanAsParameter")]
    [TestCase("WithInitializer", true, TestName = "WithInitializer")]
    [TestCase("CustomValueType", TestName = "CustomValueType")]
    [ParameterizedResourceFilter<SystemReflectionMetadataContext>("simplest")]
    public void TestStackalloc(string testFile, bool hasExplicitExpectations = false)
    {
        var options = new CecilifyTestOptions()
        {
            ResourceName = $"Expressions/Stackalloc/{testFile}",
            IgnoredILErrors = "Unverifiable|UnmanagedPointer|StackByRef" //https://github.com/adrianoc/cecilifier/issues/227
        };
        
        if (hasExplicitExpectations)
            AssertResourceTestWithExplicitExpectation(options, $"System.Void {testFile}::M()");
        else
            AssertResourceTest(options);
    }
}
