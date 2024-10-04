using System.Linq;
using System.Text.RegularExpressions;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
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
        "Emit(OpCodes.Ldarg_0);", // Load this
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

    [Test]
    public void TestReadOnlyField()
    {
        var result = RunCecilifier("class Foo { readonly int ro = 42; }");
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(
            cecilifiedCode,
            Does.Match(
                @"var fld_ro_1 = new FieldDefinition\(""ro"", .+InitOnly.+Int32\);\s+" +
                        @"cls_foo_0.Fields.Add\(fld_ro_1\);"));
    }

    [TestCase("class Foo { static int f = 42; }")]
    [TestCase("class Foo { static int f = 42; static Foo() { } }")]
    [TestCase("class Foo { static int P { get; } = 42; }")]
    [TestCase("class Foo { static int P { get; } = 42; static Foo() { } }")]
    public void StaticFields(string code)
    {
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(
            cecilifiedCode,
            Does.Match(
                @"var (cctor_foo_\d+) = new MethodDefinition\("".cctor"",.+MethodAttributes.Static.+\);\s+" +
                @"cls_foo_0.Methods.Add\(\1\);\s+"));

        Assert.That(
            cecilifiedCode,
            Does.Match(
                @"var (il_.+_\d+) = .+.Body.GetILProcessor\(\);\s+" +
                @"//static int .+ = 42;\s+" +
                @"\1.Emit\(OpCodes.Ldc_I4, 42\);\s+" +
                @"\1.Emit\(OpCodes.Stsfld, fld_.+\);"));
    }

    [Test]
    public void TesRefFieldDeclaration()
    {
        var result = RunCecilifier("ref struct RefStruct { ref int refInt; ref object o; }");
        Assert.That(
            result.GeneratedCode.ReadToEnd(), Does.Match(
            """
            var (fld_refInt_\d+) = new FieldDefinition\("refInt", FieldAttributes.Private, assembly.MainModule.TypeSystem.Int32.MakeByReferenceType\(\)\);
            \s+st_refStruct_0.Fields.Add\(\1\);
            \s+var fld_o_\d+ = new FieldDefinition\("o", FieldAttributes.Private, assembly.MainModule.TypeSystem.Object.MakeByReferenceType\(\)\);
            """));
    }
}
