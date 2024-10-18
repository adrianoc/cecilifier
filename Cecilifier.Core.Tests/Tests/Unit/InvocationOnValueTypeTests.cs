using System.Text.RegularExpressions;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;


/// <summary>
/// These tests cover scenarios in which we end up with a call on a value type.
/// </summary>
[TestFixture]
public class InvocationOnValueTypeTests : CecilifierUnitTestBase
{
    [TestCase("using System; class C { int M(int []a, Index index) { Index l = index; return a[l]; } }", "Ldloca", TestName = "Local_UsedAsIndex")]
    [TestCase("using System; class C { int M(int []a, Index index) => a[index]; }", "Ldarga", TestName = "Parameter_UsedAsIndex")]
    [TestCase("using System; class C { Index index; int M(int []a) => a[index]; }", "Ldflda", TestName = "Field_UsedAsIndex")]
    [TestCase("struct S { public int Prop {get; set; } } class C { int M(S s) => s.Prop; }", "Ldarga", TestName = "Property access on parameter")]
    [TestCase("struct S { public int Prop {get; set; } } class C { int M(S s) { S l = s; return l.Prop; } }", "Ldloca", TestName = "Property access on local")]
    [TestCase("struct S { public int Prop {get; set; } } class C { S f; int M()  => f.Prop; }", "Ldflda", TestName = "Property access on field")]
    [TestCase("using System; struct S { public event Action E; } class C { void M(){} void M(S s) { s.E +=M; } }", "Ldarga", TestName = "Event access on parameter")]
    public void TestSystemIndex(string snippet, string expectedOpCode)
    {
        var result = RunCecilifier(snippet);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var found = Regex.Matches(cecilifiedCode, expectedOpCode);
        Assert.That(found.Count, Is.EqualTo(1), $"Mismatch in expected number of {expectedOpCode}\n\n{cecilifiedCode}");
    }

    [TestCase(
        "string Get(int i) => i.ToString();", 
        """
                  (.+\.Emit\(OpCodes\.)Ldarga, .+\);
                  \1Call, .+\);\s+
                  """, 
        TestName = "CallToOverridenMethodOnPrimitive")]
    
    [TestCase(
        """
              var b = new StructNotOverridingToString();
              System.Console.WriteLine(b.ToString());

              struct StructNotOverridingToString { }
              """, 
        """
                  (.+\.Emit\(OpCodes\.)Ldloca, .+\);
                  \1Constrained, st_structNotOverridingToString_\d+\);
                  \1Callvirt, .+ToString.+\);
                  """, 
        TestName = "CallToNonOverridenMethod")]
    
    [TestCase(
        """
              struct StructOverridingToString 
              {
                  public override string ToString() => "Foo";
              }

              class Runner
              {
                 string M(StructOverridingToString b) => b.ToString(); 
              }
              """,
        """
                  (.+\.Emit\(OpCodes\.)Ldarga, .+\);
                  \1Constrained, st_structOverridingToString_\d+\);
                  \1Callvirt, m_toString_\d+\);
                  """, 
        TestName = "CallToOverridenMethodOnCustomValueType")]
        
    [TestCase(
        """
              struct S : System.IEquatable<S>
              {
                  public bool Equals(S other) => false;
              }
              
              class Runner
              {
                 bool M(S s) => s.Equals(s); 
              }
              """,
        """
                  (.+\.Emit\(OpCodes\.)Ldarga, .+\);
                  \1Ldarg_1\);
                  \1Call, m_equals_\d+\);
                  """, 
        TestName = "CallToGenericMethodOnCustomValueType")]
    
    [TestCase(
        """
                interface ITest { string Get(); }
                struct Test : ITest { public string Get() => "Test" ; }              

                class D
                {
                    string M(ITest t) => t.Get();
                }
             """,
        """
                  (.+\.Emit\(OpCodes\.)Ldarg_1\);
                  \1Callvirt, m_get_\d+\);
                  """, 
        TestName = "CallThroughInterfaceMethodOnCustomValueType")]
    public void TestCallVersusCallVirt(string snippet, string expectedCall)
    {
        var result = RunCecilifier(snippet);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var found = Regex.Matches(cecilifiedCode, expectedCall);
        Assert.That(found.Count, Is.EqualTo(1), $"Expected call instruction not found ({expectedCall})\n\n{cecilifiedCode}");
    }

    [TestCase(
        "System.Console.WriteLine(42.GetType().Name);",
        "assembly.MainModule.TypeSystem.Int32",
        "Ldloc",
        TestName = "Literal")]
    [TestCase(
        "bool b = true; System.Console.WriteLine(b.GetType().Name);",
        "assembly.MainModule.TypeSystem.Boolean",
        "Ldloc",
        TestName = "Local Variable")]
    [TestCase(
        "int Get() => 42; string M() => Get().GetType().Name;",
        "assembly.MainModule.TypeSystem.Int32",
        "Ldloc",
        TestName = "Return")]
    [TestCase(
        "string M(long l) => l.GetType().Name;",
        "assembly.MainModule.TypeSystem.Int64",
        "Ldarg",
        TestName = "Parameter")]
    [TestCase(
        "class Foo { double d; string M() => d.GetType().Name; }",
        "assembly.MainModule.TypeSystem.Double",
        "Ldfld",
        TestName = "Field")]
    [TestCase(
        "int[] ints = {1}; System.Console.WriteLine(ints[0].GetType().Name);",
        "assembly.MainModule.TypeSystem.Int32",
        "Ldelem",
        TestName = "Array Element")]
    [TestCase(
        "int i =42; ref int rf = ref i; System.Console.WriteLine(rf.GetType().Name);",
        "assembly.MainModule.TypeSystem.Int32",
        "Ldind",
        TestName = "Local Ref",
        IgnoreReason = "Corner case, does not worth the extra complexity to handle.")]
    [TestCase(
        "string M(ref int ri) => ri.GetType().Name;",
        "assembly.MainModule.TypeSystem.Int32",
        "Ldind",
        TestName = "Parameter Ref",
        IgnoreReason = "Corner case, does not worth the extra complexity to handle.")]
    public void CallGetTypeOnValueType(string snippet, string boxTargetType, string expectedLoadBeforeBox)
    {
        var result = RunCecilifier(snippet);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var expectedCall = $"""
                           (\s+.+\.Emit\(OpCodes.){expectedLoadBeforeBox}.+\);
                           \1Box, {boxTargetType}\);
                           \1Callvirt,.+ImportReference\(TypeHelpers.ResolveMethod\(typeof\(System.Object\), "GetType".+(System\.Reflection\.BindingFlags\.)Default\|\2Instance\|\2Public\)\)\);
                           """;
        var found = Regex.Matches(cecilifiedCode, expectedCall);
        Assert.That(found.Count, Is.EqualTo(1), $"Expected call instruction not found:\n{expectedCall}\n\n{cecilifiedCode}");
    }
}
