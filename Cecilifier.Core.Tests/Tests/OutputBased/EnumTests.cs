using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture]
public class EnumTests : OutputBasedTestBase
{
    [Test]
    public void TestEnum()
    { 
        AssertOutput("""
                        var e = TestEnum.First;
                        
                        System.Console.Write($"{e} {TestEnum.Second} ");
                        Print(e);
                        
                        void Print(TestEnum e)
                        {
                            System.Console.Write(e);
                        }
                        
                        enum TestEnum { First = 0x1, Second }
                        """, "First Second First");
    }
    
    [Test, Ignore("Only INT backed enums are supported for now")]
    public void TestEnumCustomStorageType()
    { 
        AssertOutput("""
                        var e = TestEnum.First;
                        System.Console.Write($"{e} {TestEnum.Second}");
                        
                        enum TestEnum : long { First = 0x100000000, Second }
                        """, "First Second");
    }
}
