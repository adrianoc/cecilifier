using System.Collections;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class TypeResolutionTests : CecilifierUnitTestBase
{
    [TestCaseSource(nameof(ExternalTypeTestScenarios))]
    public void ExternalTypeTests(string type, string expectedFullyQualifiedName, string codeTemplate)
    {
        var result = RunCecilifier(string.Format(codeTemplate, type));

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(cecilifiedCode, Contains.Substring($"ImportReference(typeof({expectedFullyQualifiedName}))"));
    }

    [TestCase(
        "class Foo : System.Collections.Generic.List<Bar> {} class Bar { Foo foo; }",
        @"cls_foo_\d+\.BaseType = .+ImportReference\(typeof\(.+Generic\.List\<\>\)\).MakeGenericInstanceType\(cls_bar_\d+\);",
        TestName = "Generic argument in base type")]

    [TestCase(
        "class Foo { System.Action<Bar> M() => null; } class Bar { Foo foo; }",
        @"m_M_\d+ = new MethodDefinition\(""M"",.+ImportReference\(typeof\(System.Action\<\>\)\)\.MakeGenericInstanceType\(cls_bar_\d+\)\);",
        TestName = "Generic argument in return type")]

    [TestCase(
        "class Foo { void M(System.Action<Bar> a) {} } class Bar { Foo foo; }",
        @"p_a_\d+ = new ParameterDefinition\(""a"", .+ImportReference\(typeof\(System\.Action\<\>\)\)\.MakeGenericInstanceType\(cls_bar_\d+\)\);",
        TestName = "Generic argument in parameter")]

    [TestCase(
        "class Foo { System.Collections.Generic.List<Bar> _bars; } class Bar { Foo foo; }",
        @".+fld__bars_\d+ = new FieldDefinition\(""_bars"",.+ImportReference\(typeof\(.+Generic\.List\<\>\)\)\.MakeGenericInstanceType\(cls_bar_\d+\)\);",
        TestName = "Generic argument in field")]

    [TestCase(
        "class Foo { System.Collections.Generic.List<Bar> Bars => null; } class Bar { Foo foo; }",
        @".+prop_bars_\d+ = new PropertyDefinition\(""Bars"",.+ImportReference\(typeof\(.+Generic\.List\<\>\)\)\.MakeGenericInstanceType\(cls_bar_\d+\)\);",
        TestName = "Generic argument in property")]

    [TestCase(
        "using System; class Foo { event EventHandler<Bar> Bar; } class Bar : EventArgs { Foo foo; }",
        @".+fld_bar_\d+ = new FieldDefinition\(""Bar"",.+ImportReference\(typeof\(.+EventHandler\<\>\)\)\.MakeGenericInstanceType\(cls_bar_\d+\)\);\s+",
        @"m_add_\d+.Parameters.Add\(new ParameterDefinition\(""value"",.+ImportReference\(typeof\(.+EventHandler\<\>\)\)\.MakeGenericInstanceType\(cls_bar_\d+\)\)\);",
        TestName = "Generic argument in event")]
    public void TypeForwardingCyclicTests(string code, params string[] expected)
    {
        // Test access to a forward type declaration is handled correctly by ensuring the value passed to MakeGenericInstanceType is a variable holding a
        // type definition (instead of resolving the type as if it was not declared in the compilation).
        // This is validated by making sure that the related member definition (field, property, parameter, event, etc) references `cls_bar_x` (i.e, the
        // variable holding the type definition for class Bar instead trying to import it like ImportReference(typeof(Bar)))
        //
        // Important:
        // All snippets used in the tests need to have 2 types referencing each other Foo <-> Bar in a way that there's no better order of 
        // processing them without requiring a forward reference.
        var result = RunCecilifier(code);
        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        foreach (var current in expected)
            Assert.That(cecilifiedCode, Does.Match(current));
    }

    [TestCase("static void Test<T>(T value) { }", @"var p_value_\d+ = new ParameterDefinition\(""value"", ParameterAttributes.None, gp_T_\d+\);", TestName = "Parameter")]
    [TestCase("static T Test<T>(T t) => t;", "m_test_6.ReturnType = gp_T_7;", TestName = "Return")]
    public void GenericTypeParameterInTopLevelMethod(string code, string testSpecificExpectation)
    {
        var result = RunCecilifier(code);

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();
        Assert.That(
            cecilifiedCode,
            Does.Match(
                @"var (gp_T_\d+) = new Mono.Cecil.GenericParameter\(""T"", (m_test_\d+)\);\s+" +
                      @"\2.GenericParameters.Add\(\1\);\s+"));

        Assert.That(
            cecilifiedCode,
            Does.Match(testSpecificExpectation));
    }

    [TestCase("DateTime field;", TestName = "Field")]
    [TestCase("DateTime Prop { get => default(DateTime); }", TestName = "Property")]
    [TestCase("DateTime M() => default(DateTime);", TestName = "Method return type")]
    [TestCase("void M(DateTime d) {}", TestName = "Parameter")]
    [TestCase("void M() { DateTime d; }", TestName = "Local variable")]
    [TestCase("void M() { Action<DateTime> d; }", TestName = "Local variable (generic)")]
    [TestCase("void M() { Delegate d; }", TestName = "Delegate")]
    public void TestTypeResolution_Issue277(string memberDefinition)
    {
        var result = RunCecilifier($$"""using System; class C { {{memberDefinition}} }""");
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Not.Match(@"assembly\.MainModule\.TypeSystem\.DateTime"));
    }

    private static IEnumerable ExternalTypeTestScenarios()
    {
        const string fieldTemplate = @"using System.Collections; class Foo {{ {0} field; }}";
        const string propertyTemplate = @"using System.Collections; class Foo {{ {0} Property {{ get; set; }} }}";
        const string methodTemplate = @"using System.Collections; class Foo {{ {0} Method() => default; }}";
        const string typeOfTemplate = @"using System.Collections; class Foo {{ object Method() => typeof({0}); }}";
        const string genericConstraintTemplate = @"using System.Collections; class Foo<T> where T : {0} {{ }}";

        // Types in this list needs to be valid in generic constraints
        var typesToTest = new[]
        {
            ("System.Net.IWebProxy", "System.Net.IWebProxy", "Fully Qualified"),
            ("ArrayList", "System.Collections.ArrayList", "Simple Name"),
            ("System.Collections.Generic.IList<int>", "System.Collections.Generic.IList<>", "Generic Name"),
        };

        var valueTuples = new[]
        {
            ("Field", fieldTemplate),
            ("Property", propertyTemplate),
            ("Method", methodTemplate),
            ("typeof()", typeOfTemplate),
            ("Generic Constraint", genericConstraintTemplate),
        };

        foreach (var (declared, expected, nameKind) in typesToTest)
            foreach (var (memberKind, template) in valueTuples)
                yield return new TestCaseData(declared, expected, template).SetName($"{memberKind} - {nameKind}");
    }
}
