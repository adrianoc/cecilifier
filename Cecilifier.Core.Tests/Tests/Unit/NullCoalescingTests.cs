using System.Text.RegularExpressions;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class NullCoalescingTests : CecilifierUnitTestBase
{
    [Test]
    public void Simplest_ReferenceTypes()
    {
        var result = RunCecilifier("object M(object o1, object o2) => o1 ?? o2;");
        Assert.That(
            result.GeneratedCode.ReadToEnd(), 
            Does.Match("""
                       //o1 \?\? o2
                       \s+var return_\d+ = (?<il>il_M_\d+)\.Create\(OpCodes.Nop\);
                       (?<emit>\s+\k<il>\.Emit\(OpCodes\.)Ldarg_0\);
                       \k<emit>Dup\);
                       \k<emit>Brtrue_S, return_\d+\);
                       \k<emit>Pop\);
                       \k<emit>Ldarg_1\);
                       \s+\k<il>\.Body\.Instructions\.Add\(return_\d+\);
                       \k<emit>Ret\);
                       """));
    }
    
    [Test]
    public void LeftExpression_IsEvaluated_OnlyOnce()
    {
        var result = RunCecilifier("object M2(int i) => i > 10 ? null : new object(); object M(int n, object o2) => M2(n) ?? o2;");
        Assert.That(
            result.GeneratedCode.ReadToEnd(), 
            Does.Match("""
                       //M2\(n\) \?\? o2
                       \s+var return_\d+ = (?<il>il_M_\d+\.)Create\(OpCodes.Nop\);
                       (?<emit>\s+\k<il>Emit\(OpCodes\.)Ldarg_0\);
                       \k<emit>Call, m_m2_\d+\);
                       \k<emit>Dup\);
                       \k<emit>Brtrue_S, return_\d+\);
                       \k<emit>Pop\);
                       \k<emit>Ldarg_1\);
                       \s+\k<il>Body\.Instructions.Add\(return_\d+\);
                       \k<emit>Ret\);
                       \s+//End of local function
                       """));
    }
    
    [Test]
    public void SimpleNullableValueType()
    {
        var result = RunCecilifier("int? M(int? i1, int? i2) => i1 ?? i2;");
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match("""
                                                                 //i1 \?\? i2
                                                                 (?<emit>\s+il_M_\d+\.Emit\(OpCodes\.)Ldarg_0\);
                                                                 \s+var (?<left>l_leftValue_\d+) = new VariableDefinition\(.+ImportReference\(.+Nullable<>\)\)\.MakeGenericInstanceType\(.+Int32\)\);
                                                                 \s+m_M_\d+.Body.Variables.Add\(\k<left>\);
                                                                 \k<emit>Stloc, \k<left>\);
                                                                 \k<emit>Ldloca_S, \k<left>\);
                                                                 \k<emit>Call,.+typeof\(System.Nullable<System.Int32>\).+"get_HasValue".+\);
                                                                 \s+var (?<loadLeftValue>loadLeftValueTarget_\d+) = il_M_\d+.Create\(OpCodes.Ldloc_S, \k<left>\);
                                                                 \k<emit>Brtrue_S, \k<loadLeftValue>\);
                                                                 \k<emit>Ldarg_1\);
                                                                 \k<emit>Ret\);
                                                                 \s+il_M_\d+\.Body\.Instructions.Add\(\k<loadLeftValue>\);
                                                                 \k<emit>Ret\);
                                                                 """));
    }
    
    [Test]
    public void LeftExpression_IsEvaluated_OnlyOnce_ValueType()
    {
        var result = RunCecilifier("int? M2(int? i) => i > 10 ? null : i; int? M(int? n) => M2(n) ?? n;");
        var cecilified = result.GeneratedCode.ReadToEnd();
        var matches = Regex.Matches(cecilified!, @"il_M_\d+\.Emit\(OpCodes\.Call, m_m2_\d+\);");
        Assert.That(matches.Count, Is.EqualTo(1), cecilified);
    }
    
    [Test]
    public void MixedNullableValueType_AndReferenceType()
    {
        var result = RunCecilifier("""
                                   var r = M3(42, 1);
                                   int? M3(int? i1, object i2) => i1 ?? (int) i2;
                                   """);
        Assert.That(
            result.GeneratedCode.ReadToEnd(),
            Does.Match("""
                       (?<emit>\s+il_m3_\d+\.Emit\(OpCodes\.)Brtrue_S, (?<loadLeftValue>loadLeftValueTarget_\d+)\);
                       \k<emit>Ldarg_1\);
                       \k<emit>Unbox_Any, assembly.MainModule.TypeSystem.Int32\);
                       \k<emit>Newobj,.+System.Nullable<>.+MakeGenericType\(typeof\(System.Int32\)\).GetConstructors\(\).Single\(ctor => ctor.GetParameters\(\).Length == 1\)\)\);
                       \k<emit>Ret\);
                       \s+il_m3_\d+\.Body\.Instructions\.Add\(\k<loadLeftValue>\);
                       \k<emit>Ret\);
                       \s+//End of local function\.
                       """));
    }
}
