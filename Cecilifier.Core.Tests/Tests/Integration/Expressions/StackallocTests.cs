using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration;

[TestFixture(typeof(MonoCecilContext))]
public class StackallocTests<TResource> : ResourceTestBase<TResource> where TResource : IVisitorContext
{
    [TestCase("simplest", TestName = "Simplest")]
    [TestCase("WithSpan", TestName = "WithSpan")]
    [TestCase("WithSpanAsParameter", true, TestName = "WithSpanAsParameter")]
    [TestCase("WithInitializer", true, TestName = "WithInitializer")]
    [TestCase("CustomValueType", TestName = "CustomValueType")]
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
