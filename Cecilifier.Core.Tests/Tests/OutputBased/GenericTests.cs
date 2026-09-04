using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using Cecilifier.Core.Tests.Framework.Attributes;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture(typeof(MonoCecilContext))]
[TestFixture(typeof(SystemReflectionMetadataContext))]
public class GenericTests<TContext> : OutputBasedTestBase<TContext> where TContext : IVisitorContext
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
    
    [Test]
    [DisableForContext<SystemReflectionMetadataContext>(IgnoreReason = "Throws `Metadata table GenericParam not sorted.` (#375)")]
    public void TypeParameterMetadataTables_AreSorted()
    {
        AssertOutput("""
                     using System;
                     using System.Collections.Generic;
                     
                     Console.Write($"{TestSubject<char>.M1(42)} {TestSubject<char>.M2(42)}");
                     
                     class TestSubject<TC>
                     {
                         // This fails
                         public static int M1(int v) => v;
                         public static T M2<T>(T v) => v;
                         
                         // This works! :/
                         //public static T M2<T>(T v) => v;
                         //public static int M1(int v) => v;
                     }
                     """, 
            "42 42");
    }
}
