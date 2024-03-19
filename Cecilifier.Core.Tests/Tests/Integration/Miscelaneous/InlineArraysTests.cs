using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration;

public class InlineArraysTests : ResourceTestBase
{
    [Test]
    public void TestInlineArrays()
    {
        AssertResourceTestWithExplicitExpectation("Misc/InlineArrays", "System.Void InlineArrayTests::Test()");
    }
}
