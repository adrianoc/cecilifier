using System.Collections;
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
        
        var valueTuples = new []
        {
            ("Field", fieldTemplate), 
            ("Property", propertyTemplate),
            ("Method", methodTemplate),
            ("typeof()", typeOfTemplate),
            ("Generic Constraint", genericConstraintTemplate),
        };
        
        foreach(var (declared, expected, nameKind) in typesToTest)
        foreach (var (memberKind, template) in valueTuples)
            yield return new TestCaseData(declared, expected, template).SetName($"{memberKind} - {nameKind}");
    }
}
