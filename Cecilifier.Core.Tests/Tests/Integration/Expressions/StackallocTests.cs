using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration;

[TestFixture]
public class StackallocTests : IntegrationResourceBasedTest
{
    
    [TestCase("simplest", TestName = "Simplest")]
    [TestCase("WithSpan", TestName = "WithSpan")]
    [TestCase("WithSpanAsParameter", true, TestName = "WithSpanAsParameter")]
    [TestCase("WithInitializer", true, TestName = "WithInitializer")]
    [TestCase("CustomValueType", TestName = "CustomValueType")]
    public void TestStackalloc(string testFile, bool hasExplicitExpectations = false)
    {
        if (hasExplicitExpectations)
            AssertResourceTestWithExplicitExpectation($"Expressions/Stackalloc/{testFile}", $"System.Void {testFile}::M()");
        else
            AssertResourceTest($"Expressions/Stackalloc/{testFile}");
    }
}
