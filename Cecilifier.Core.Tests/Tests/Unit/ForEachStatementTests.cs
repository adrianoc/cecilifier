using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
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
                                   foreach(var v in new Enumerator()) {}
                                   
                                   public {{enumeratorKind}} Enumerator
                                   {
                                        public int Current => 1;
                                        public bool MoveNext() => false;

                                        public Enumerator GetEnumerator() => default(Enumerator);
                                   }
                                   """);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match("""
                                               \s+//foreach\(var v in new Enumerator\(\)\) {}
                                               \s+il_topLevelMain_\d+.Emit\(OpCodes.Newobj, ctor_enumerator_\d+\);
                                               """), "enumerator type defined in the snippet should be used.");
        
        Assert.That(cecilifiedCode, Does.Match("""
                                               \s+//variable to store the returned 'IEnumerator<T>'.
                                               \s+il_topLevelMain_\d+.Emit\(OpCodes.Callvirt, m_getEnumerator_\d+\);
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
                                               """), "IEnumerable<int>.GetEnumerator() should be used.");
    }
    
    [Test]
    public void EnumerableImplementingGenericAndNonGenericIEnumerator()
    {
        // The difference from this test to the one above is very small, but very important: this test checks that List<T>.Enumerable, a value type
        // implementing the enumerator pattern ...
        var listOfTEnumerator = typeof(List<>.Enumerator);

        var expected= new[] { typeof(IEnumerator<>), typeof(IEnumerator), typeof(IDisposable) };
        Assert.That(
            listOfTEnumerator.GetInterfaces().Select(itf => itf.IsConstructedGenericType ? itf.GetGenericTypeDefinition() : itf),
            Is.EquivalentTo(expected));
        
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
        Assert.That(cecilifiedCode, Does.Match("""var l_openget_Current_\d+ = .+ImportReference\(typeof\(.+IEnumerator<>\)\).Resolve\(\).Methods.First\(m => m.Name == "get_Current" && m.Parameters.Count == 0 \);"""));
    }
 
    [Test]
    public void Array()
    {
        var result = RunCecilifier("""
                                   foreach(var v in new int[] {1, 2, 3, 4})
                                        System.Console.WriteLine(v);
                                   """);
        
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        // this test assumes that if the IL to check the limit of the loop is present
        // then the foreach was processed as an array access. 
        Assert.That(
            cecilifiedCode, 
            Does.Match("""
                       (il_topLevelMain_\d+\.)Append\(nop_\d+\);
                       (\s+\1Emit\(OpCodes\.)Ldloc, l_index_\d+\);
                       \2Ldloc, l_array_\d+\);
                       \2Ldlen\);
                       \2Conv_I4\);
                       \2Blt, ldloc_\d+\);
                       """), 
            "Array loop index check code does not match");
         
    }
    
    [Test]
    public void IDisposableStructEnumerator()
    {
        var result = RunCecilifier("""
                                   foreach(var v in new Enumerator()) 
                                        System.Console.WriteLine(v);

                                   struct Enumerator : System.IDisposable
                                   {
                                        public int Current => 1;
                                        public bool MoveNext() => false;
                                        public void Dispose() {}
     
                                        public Enumerator GetEnumerator() => default;
                                   }
                                   """);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match("""
                                               //finally start
                                               \s+var nop_\d+ = (?<il>il_topLevelMain_\d+\.)Create\(OpCodes.Nop\);
                                               \s+\k<il>Append\(nop_\d+\);
                                               (?<emit>\s+\k<il>Emit\(OpCodes\.)Ldloca, l_enumerator_\d+\);
                                               \k<emit>Constrained, st_enumerator_0\);
                                               \k<emit>Callvirt, .+ImportReference\(.+ResolveMethod\(typeof\(System.IDisposable\), "Dispose",.+\)\)\);
                                               \k<emit>Endfinally\);
                                               """), "no box, call to IDisposable.Dispose() is constrained");
    }
    
    [Test]
    public void IDisposableClassEnumerator()
    {
        var result = RunCecilifier("""
                                   class Enumerator : System.IDisposable
                                   {
                                        public int Current => 1;
                                        public bool MoveNext() => false;
                                        public void Dispose() {}

                                        public Enumerator GetEnumerator() => default;
                                   }

                                   class Driver
                                   {
                                        int Use(int t)
                                        {
                                            foreach(var v in new Enumerator())
                                                t += v;
                                            return t;
                                        }
                                   }
                                   """);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Does.Match("""
                                               //finally start
                                               \s+var nop_22 = il_use_14.Create\(OpCodes.Nop\);
                                               \s+(il_use_\d+)\.Append\(nop_22\);
                                               \s+var (?<skip_disposable>nop_\d+) = \1.Create\(OpCodes.Nop\);
                                               (?<e>\s+\1\.Emit\(OpCodes\.)Ldloc, (?<enum_var>l_enumerator_\d+)\);
                                               \3Brfalse_S, \k<skip_disposable>\);
                                               \3Ldloc, \k<enum_var>\);
                                               \3Callvirt, .+ImportReference\(.+ResolveMethod\(typeof\(System.IDisposable\), "Dispose",.+\)\)\);
                                               \s+\1\.Append\(\k<skip_disposable>\);
                                               \3Endfinally\);
                                               """));
    }
    
    [Test]
    public void Sealed_NonIDisposable_ClassEnumerator()
    {
        var result = RunCecilifier("""
                                   sealed class Enumerator
                                   {
                                        public int Current => 1;
                                        public bool MoveNext() => false;

                                        public Enumerator GetEnumerator() => default;
                                   }

                                   class Driver
                                   {
                                        int Use(int t)
                                        {
                                            foreach(var v in new Enumerator())
                                                t += v;
                                            return t;
                                        }
                                   }
                                   """);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(
            cecilifiedCode, Does.Not.Match(@"OpCodes\.Endfinally|Body\.ExceptionHandlers\.Add"), 
            "Ensures finally is not emitted (nothing to dispose)");
    }
}
