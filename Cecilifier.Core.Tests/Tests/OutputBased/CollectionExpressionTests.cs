using System.Text.RegularExpressions;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

public class CollectionExpressionTests(IILGeneratorApiDriver driver) : OutputBasedTestBase(driver)
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
    public void ReferenceTypes()
    {
        AssertOutput(
            """
            using System.Collections.Generic;
            List<Bar> list = [new Bar(1), new Bar(2)];
            
            for(List<Bar>.Enumerator enumerator = list.GetEnumerator(); enumerator.MoveNext();)
                System.Console.Write(enumerator.Current);
            
            class Bar 
            {
                public Bar(int i) => Value = i;
                public override string ToString() => Value.ToString();
                public int Value;
            }
            """, 
            "12");
        
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
    public void ImplicitUserDefinedConversions_Are_Applied([Values("List<Foo>", "Foo[]", "Span<Foo>")] string targetType, [Values("[2, 1]", "[5, 4, 3, 2, 1]")] string items)
    {
        AssertConversionIsApplied(targetType, items, "Foo");
    }
    
    [Test]
    public void BoxConversions_Are_Applied([Values("List<object>", "object[]", "Span<object>")] string targetType, [Values("[2, 1]", "[5, 4, 3, 2, 1]")] string items)
    {
        AssertConversionIsApplied(targetType, items, "object");
    }

    void AssertConversionIsApplied(string targetType, string items, string elementType)
    {
        var (lengthExtractor, expectedILError) = targetType == $"Span<{elementType}>" ? ("items.Length", "ReturnPtrToStack") : ("((ICollection) items).Count", null);
        AssertOutput(
            $$"""
              using System.Collections.Generic;
              using System.Collections;
              using System;

              {{targetType}} items = {{items}};
              // We can´t use a foreach (to simplify the code) due to problems
              for(var i = 0; i < {{lengthExtractor}}; i++) System.Console.Write(items[i]);
              
              struct Foo
              {
                  public Foo(int i) => Value = i;
                  public static implicit operator Foo(int i) => new Foo(i);
                  public static implicit operator int(Foo f) => f.Value;
                  public int Value;
              }
              """,
            Regex.Replace(items, @"[\[\],\s+]", ""),
            expectedILError);
    }
}
