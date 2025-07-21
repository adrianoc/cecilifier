using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

public class MemberAccessTests(IILGeneratorApiDriver driver) : OutputBasedTestBase(driver)
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
}
