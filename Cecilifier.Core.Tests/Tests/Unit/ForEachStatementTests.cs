using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class ForEachStatementTests : CecilifierUnitTestBase
{
    // https://cutt.ly/swrhz6VE
    //[TestCase("struct")]
    [TestCase("sealed class")]
    public void NonDisposableGetEnumeratorPattern(string enumeratorKind)
    {
        // Compiler uses GetEnumerator() method, does not require implementing IEnumerable<T>
        var result = RunCecilifier($$"""
                                   public {{enumeratorKind}} Enumerator
                                   {
                                        public int Current => 1;
                                        public bool MoveNext() => false;

                                        public Enumerator GetEnumerator() => default(Enumerator);
                                   }
                                   
                                   //TODO: change to top level statements when order of visiting of top level/classes gets fixed. 
                                   class Driver
                                   {
                                       static void Main()
                                       {
                                            foreach(var v in new Enumerator()) {}
                                       }
                                   }
                                   """);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match("""
                                               \s+//foreach\(var v in new Enumerator\(\)\) {}
                                               \s+il_main_\d+.Emit\(OpCodes.Newobj, ctor_enumerator_\d+\);
                                               """), "enumerator type defined in the snippet should be used.");
        
        Assert.That(cecilifiedCode, Does.Match("""
                                               \s+//variable to store the returned 'IEnumerator<T>'.
                                               \s+il_main_\d+.Emit\(OpCodes.Callvirt, m_getEnumerator_\d+\);
                                               """), "GetEnumerator() defined in the snippet should be used.");
    }
}
