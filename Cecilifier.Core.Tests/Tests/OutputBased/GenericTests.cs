using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

public class GenericTests(IILGeneratorApiDriver driver) : OutputBasedTestBase(driver)
{
    [Test]
    public void NonGenericTypedProperty_OnGenericType_Works()
    {
        AssertOutput("""
                     class Foo<T>
                     {
                         public int Value => 42;
                     }
                     
                     class Runner 
                     {
                        static void Main()
                        {
                            var f = new Foo<int>();
                            System.Console.WriteLine(f.Value);
                        }
                     }
                     """, 
            "42");
    }

    [Test]
    public void GenericInstanceMethod_ReferencingTypeParametersFromDeclaringType_Works()
    {
        AssertOutput("""
                           using System;
                           using System.Collections.Generic;
                           using System.Linq;

                           List<int> ints = new List<int>() { 1,2,3 };
                           var strings = ints.ConvertAll(FromInt);
                           // We need to call ToArray() due to a bug in cecilifier that crashes if List<T>.GetEnumerator()
                           // is used in a foreach. 
                           foreach(var s in strings.ToArray()) 
                                Console.Write(s);

                           static string FromInt(int i) => i.ToString();
                           """, 
            "123");
    }
    
}
