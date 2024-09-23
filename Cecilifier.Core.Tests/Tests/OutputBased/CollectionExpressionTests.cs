using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

public class CollectionExpressionTests : OutputBasedTestBase
{
    [Test]
    public void ArrayWith3OrMoreElements()
    {
        AssertOutput("int[] mediumArray = [1, 2, 3]; System.Console.WriteLine(mediumArray[0] + mediumArray[2]);", "4");
    }
    
    [Test]
    public void ArrayWith2OrLessElements()
    {
        AssertOutput("int[] mediumArray = [1, 2]; System.Console.WriteLine(mediumArray[0] + mediumArray[1]);", "3");
    }
}
