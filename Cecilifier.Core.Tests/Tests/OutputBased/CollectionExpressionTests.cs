using System;
using System.Text.RegularExpressions;
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
    
    [Test]
    public void ImplicitNumericConversions_Are_Applied([Values("List<long>", "long[]", "Span<long>")] string targetType, [Values("[2, 1]", "[5, 4, 3, 2, 1]")] string items)
    {
        AssertOutput(
            $"""
            using System.Collections.Generic;
            using System;
            
            {targetType} items = {items}; 
            foreach(var c in items) System.Console.Write(c);
            """, 
            Regex.Replace(items, @"\s+|\[|\]|,", ""),
            "ReturnPtrToStack" // This is required only for spans.
        );
    }
    
    [Test]
    public void ImplicitTypeConversions_Are_Applied([Values("List<Foo>", "Foo[]", "Span<Foo>")] string targetType, [Values("[2, 1]", "[5, 4, 3, 2, 1]")] string items)
    {
        var (lengthExtractor, expectedILError) = targetType == "Span<Foo>" ? ("items.Length", "[ReturnPtrToStack]") : ("((ICollection<Foo>) items).Count", null);
        AssertOutput(
            $$"""
              using System.Collections.Generic;
              using System;

              {{targetType}} items = [1, 2];
              // We can´t rely on a foreach (to simplify the code) due to issue #306
              for(var i = {{lengthExtractor}} - 1 ; i >= 0; i--) System.Console.Write(items[i].Value);

              struct Foo
              {
                  public Foo(int i) => Value = i;
                  public static implicit operator Foo(int i) => new Foo(i);
                  public int Value;
              }
              """,
            "21",
            expectedILError);
    }
}
