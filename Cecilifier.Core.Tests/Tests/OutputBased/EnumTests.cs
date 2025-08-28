using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.ApiDriver.SystemReflectionMetadata;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture(typeof(MonoCecilContext))]
[TestFixture(typeof(SystemReflectionMetadataContext))]
[EnableForContext<SystemReflectionMetadataContext>(IgnoreReason = "Not implemented yet")]
public class EnumTests<TContext> : OutputBasedTestBase<TContext> where TContext : IVisitorContext
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
