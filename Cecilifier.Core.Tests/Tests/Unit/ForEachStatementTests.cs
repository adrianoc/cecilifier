using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

    [Test]
    public void GenericEnumerable()
    {
        var result = RunCecilifier("""
                                   using System;
                                   class Foo
                                   {
                                        public void M(System.Collections.Generic.IList<int> e)
                                        {
                                            foreach(var v in e)
                                                Console.WriteLine(v);
                                        }
                                   }
                                   """);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match("""
                                               \s+//foreach\(var v in e\)...
                                               \s+il_M_2.Emit\(OpCodes.Ldarg_1\);
                                               """), "enumerable passed to the method should be used.");
        
        Assert.That(cecilifiedCode, Does.Match("""
                                               \s+//variable to store the returned 'IEnumerator<T>'.
                                               \s+il_M_\d+.Emit\(OpCodes.Callvirt, .+ImportReference\(.+ResolveMethod\(.+System.Collections.Generic.IEnumerable<System.Int32>.+, "GetEnumerator",.+\)\)\);
                                               """), "IEnumerable<int>.GetEnumerator() defined in the snippet should be used.");
    }
    
    [Test]
    public void EnumerableImplementingGenericAndNonGenericIEnumerator()
    {
        // The difference from this test to the one above is very small, but very important: this test checks that List<T>.Enumerable, a value type
        // implementing the enumerator pattern ...
        var listOfTEnumerator = typeof(List<>.Enumerator);

        var expected= new[] { typeof(IEnumerator<>), typeof(IEnumerator), typeof(IDisposable) };
        CollectionAssert.AreEquivalent(
            expected,
            listOfTEnumerator.GetInterfaces().Select(itf => itf.IsConstructedGenericType ? itf.GetGenericTypeDefinition() : itf));
        
        var result = RunCecilifier("""
                                   using System;
                                   class Foo
                                   {
                                        public void M(System.Collections.Generic.List<int> e)
                                        {
                                            foreach(var v in e)
                                                Console.WriteLine(v);
                                        }
                                   }
                                   """);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match("""
                                               //variable to store the returned 'IEnumerator<T>'.
                                               \s+il_M_\d+.Emit\(OpCodes.Callvirt, .+ImportReference\(.+ResolveMethod\(typeof\(System.Collections.Generic.List<System.Int32>\), "GetEnumerator",.+\)\)\);
                                               \s+var l_enumerator_\d+ = new VariableDefinition\(.+ImportReference\(typeof\(System.Collections.Generic.List<int>.Enumerator\)\)\);
                                               """));
        
        Assert.That(cecilifiedCode, Does.Match("""
                                               il_M_\d+.Emit\(OpCodes.Ldloca, l_enumerator_\d+\);
                                               \s+il_M_\d+.Emit\(OpCodes.Call, .+ImportReference\(.+ResolveMethod\(typeof\(.+List<System.Int32>.Enumerator\), "MoveNext",.+\)\)\);
                                               """));
        
        Assert.That(cecilifiedCode, Does.Match("""
                                               il_M_\d+.Emit\(OpCodes.Ldloca, l_enumerator_\d+\);
                                               \s+il_M_\d+.Emit\(OpCodes.Call, .+ImportReference\(.+ResolveMethod\(typeof\(.+List<System.Int32>.Enumerator\), "get_Current",.+\)\)\);
                                               """));
    }
    
    // I've considered adding a test for instantiated IEnumerable<T> (for instance, IEnumerable<int>) but it doesn't look like to add any value since the generated code
    // is very similar to the one in this test and any open/closed differences should be covered by generics handling.
    [Test]
    public void OpenIEnumerable()
    {
        var result = RunCecilifier("void Run<T>(System.Collections.Generic.IEnumerable<T> e) { foreach(var v in e) {} }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilifiedCode, Does.Match("""
                                               il_run_\d+.Emit\(OpCodes.Callvirt, .+ImportReference\(.+ResolveMethod\(typeof\(System.Collections.IEnumerator\), "MoveNext",.+\)\)\);
                                               """));
        Assert.That(cecilifiedCode, Does.Match("""var l_openget_Current_\d+ = .+ImportReference\(typeof\(.+IEnumerator<>\)\).Resolve\(\).Methods.First\(m => m.Name == "get_Current"\);"""));
        
        /*
        IL_0000: ldarg.0
        IL_0001: callvirt instance class [System.Runtime]System.Collections.Generic.IEnumerator`1<!0> class [System.Runtime]System.Collections.Generic.IEnumerable`1<!!T>::GetEnumerator()
        IL_0006: stloc.0
        .try
        {
            // sequence point: hidden
            IL_0007: br.s IL_0010
            // loop start (head: IL_0010)
                IL_0009: ldloc.0
                IL_000a: callvirt instance !0 class [System.Runtime]System.Collections.Generic.IEnumerator`1<!!T>::get_Current()
                IL_000f: pop

                IL_0010: ldloc.0
                IL_0011: callvirt instance bool [System.Runtime]System.Collections.IEnumerator::MoveNext()
                IL_0016: brtrue.s IL_0009
            // end loop

            IL_0018: leave.s IL_0024
        } // end .try
        finally
        {
            // sequence point: hidden
            IL_001a: ldloc.0
            IL_001b: brfalse.s IL_0023

            IL_001d: ldloc.0
            IL_001e: callvirt instance void [System.Runtime]System.IDisposable::Dispose()

            // sequence point: hidden
            IL_0023: endfinally
        } // end handler         
         */
    }
}
