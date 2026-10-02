using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using Cecilifier.Core.Tests.Framework.Attributes;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture(typeof(MonoCecilContext))]
[TestFixture(typeof(SystemReflectionMetadataContext))]
[EnableForContext<SystemReflectionMetadataContext>(nameof(EventDefinedInGenericType), IgnoreReason = "Not implemented yet")]
public class MemberAccessTests<TContext> : OutputBasedTestBase<TContext> where TContext : IVisitorContext
{
    [Test]
    public void GenericRefMemberReferences_Issue340()
    {
        AssertOutput("""
                    using System;
                    using System.Runtime.CompilerServices;
                    
                    var n = 10;
                    var f = new Ref<int>();
                    f.SetItem(ref n);
                    f.Print();
                    n = 31;
                    f.Print();
                    
                    ref struct Ref<T>
                    {
                        private ref T item;
                    
                        unsafe public void SetItem(ref T v)
                        {
                            item = ref Unsafe.AsRef<T>(ref v);
                        }
                    
                        public void Print() => System.Console.Write(item);
                    }
                    """, "1031");
        
    }

    [Test]
    public void EventDefinedInGenericType()
    {
        AssertOutput("""
                     var f = new Foo<int>();
                     f.TheEvent += Print;
                     f.Trigger(42);
                     
                     static void Print(int i) => System.Console.Write(i);
                     struct Foo<T>
                     {
                        public event System.Action<T> TheEvent;
                        
                        public void Trigger(T t) => TheEvent.Invoke(t);
                     }
                     """,
            "42");
    }
}
