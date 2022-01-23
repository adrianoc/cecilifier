using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class FieldsTests : CecilifierUnitTestBase
{
    [Test]
    public void TestExternalFields()
    {
        const string code = "class ExternalStaticFieldsAccess { string S() => string.Empty; }";
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        
        Assert.That(cecilifiedCode, Contains.Substring("il_S_2.Emit(OpCodes.Ldsfld, assembly.MainModule.ImportReference(TypeHelpers.ResolveField(\"System.String\",\"Empty\")));"));
    }
    
    [TestCase(
        "class Foo { int Value; void M(Foo other) => other.Value = 42; }", 
        "Emit(OpCodes.Ldarg_1);", // load `other` (1st method arg) 
        "Emit(OpCodes.Ldc_I4, 42);", // Load 42
        "Emit(OpCodes.Stfld, fld_value_1);", // Store in other.Value
        TestName = "Deep Member Access")]
    
    [TestCase(
        "class Foo { int Value; void M() => Value = 42; }", 
        "Append(ldarg_0_4);", // Load this
        "Emit(OpCodes.Ldc_I4, 42);",  // Load 42
        "Emit(OpCodes.Stfld, fld_value_1);", // Store in Value
        TestName = "Implicit This")]
    
    [TestCase(
        "class Foo { int Value; void M() => this.Value = 42; }", 
        "Emit(OpCodes.Ldarg_0);", // Load this
        "Emit(OpCodes.Ldc_I4, 42);",  // Load 42
        "Emit(OpCodes.Stfld, fld_value_1);", // Store in this.Value
        TestName = "Explicit This")]
    
    [TestCase(
        "class Foo { static int Value; void M() => Foo.Value = 42; }", 
        "Emit(OpCodes.Ldc_I4, 42);", // Load 42 
        "Emit(OpCodes.Stsfld, fld_value_1);",  // Store in Foo.Value
        TestName = "Static Field")]
    public void TestFieldAsMemberReferences(string code, params string[] instructions)
    {
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(instructions, Is.Not.Empty, cecilifiedCode);
        
        var expectedSnippet = instructions.Aggregate("", (acc, curr) => acc + "\\s*.+\\." + Regex.Escape(curr));
        Assert.That(cecilifiedCode, Does.Match(expectedSnippet));
    }

    // [Test]
    // public void TestExternalInstanceFields()
    // {
    //     const string code = "class ExternalInstanceFields { int Instance(System.ValueTuple<int> t) => t.Item1; }";
    //     var result = RunCecilifier(code);
    //     var cecilifiedCode = result.GeneratedCode.ReadToEnd();
    //     
    //     Assert.That(cecilifiedCode, Contains.Substring("XX FORCE IT TO FAIL XX;"));
    // }
}
