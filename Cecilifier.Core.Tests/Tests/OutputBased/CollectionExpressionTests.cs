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
    
    [Test]
    public void Span()
    {
        AssertOutput(
            "System.Span<int> span = [1, 2, 3]; System.Console.WriteLine(span[0] + span[2]);", 
            "4", 
            "ReturnPtrToStack" // Seems like an issue with ILVerify since verifying the code above compiled with C# compiler
                                                  // generates the same error. 
            );
    }
    
    [Test]
    public void SpanAsParameter()
    {
        AssertOutput(
            """
            Print([1, 2, 3]);
            static void Print(System.Span<int> span) => System.Console.WriteLine(span[0] + span[2]);
            """, 
            "4", 
            "ReturnPtrToStack" // Seems like an issue with ILVerify since verifying the code above compiled with C# compiler
                                                  // generates the same error. 
            );
    }
    
    [Test]
    public void ListOfT()
    {
        AssertOutput(
            """
            System.Collections.Generic.List<char> list = ['C', 'E', 'C', 'I', 'L'];
            foreach(var c in list.ToArray()) System.Console.Write(c);
            """, 
            "CECIL");
    }
}
