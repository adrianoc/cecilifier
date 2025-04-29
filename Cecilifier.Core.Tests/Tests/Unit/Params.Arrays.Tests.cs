#nullable enable
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class ParamsTests : CecilifierUnitTestBase
{
    [TestCaseSource(nameof(CallSiteTestScenarios))]
    public void CallSite(string paramsType, string expectedRegex)
    {
        var result = RunCecilifier($$"""
                                   using System.Collections.Generic;
                                   using System;
                                   
                                   class Foo 
                                   {
                                        void Use() => M(1, 2, 3);
                                        void M(params {{paramsType}} items) { }
                                   }
                                   """);
        
        Assert.That(result.GeneratedCode.ReadToEnd(), Does.Match(expectedRegex));
    }
    
    static IEnumerable<TestCaseData> CallSiteTestScenarios()
    {
        yield return new TestCaseData(
            "int[]",
            """
            //M\(1, 2, 3\)
            (?<emit>\s+il_use_\d+\.Emit\(OpCodes\.)Ldarg_0\);
            \s+var (?<paramsVar>l_itemsParams_\d+) = new VariableDefinition\(.+Int32.MakeArrayType\(\)\);
            \s+m_use_\d+.Body.Variables.Add\(\k<paramsVar>\);
            \k<emit>Ldc_I4, 3\);
            """).SetName("int[]");
        
        yield return new TestCaseData(
            "Span<int>",
            """
            //M\(1, 2, 3\)
            (?<emit>\s+il_use_\d+\.Emit\(OpCodes\.)Ldarg_0\);
            \s+//InlineArray to store the `params` values.
            """).SetName("Span<int>");
        
        yield return new TestCaseData(
            "ReadOnlySpan<int>",
            """
            
            """).SetName("ReadOnlySpan<int>").Ignore("placeholder");
        
        yield return new TestCaseData(
            "IList<int>",
            """
            
            """).SetName("IList<int>").Ignore("placeholder");
        
        yield return new TestCaseData(
            "ICollection<int>",
            """
            
            """).SetName("ICollection<int>").Ignore("placeholder");
        
        yield return new TestCaseData(
            "IEnumerable<int>",
            """
            
            """).SetName("IEnumerable<int>").Ignore("placeholder");
    }

    [TestCase("int[]", "ParamArrayAttribute", @".+assembly\.MainModule\.TypeSystem\.Int32\.MakeArrayType\(\)")]
    [TestCase("Span<int>", null, @".+ImportReference\(typeof\(.+Span<>\)\)\.MakeGenericInstanceType\(.+Int32\)")]
    [TestCase("ReadOnlySpan<int>", null, @".+ImportReference\(typeof\(.+ReadOnlySpan<>\)\)\.MakeGenericInstanceType\(.+Int32\)", IgnoreReason = "Placeholder")]
    [TestCase("IList<int>", null, @".+ImportReference\(typeof\(.+IList<>\)\)\.MakeGenericInstanceType\(.+Int32\)", IgnoreReason = "Placeholder")]
    [TestCase("ICollection<int>", null, @".+ImportReference\(typeof\(.+ICollection<>\)\)\.MakeGenericInstanceType\(.+Int32\)", IgnoreReason = "Placeholder")]
    [TestCase("IEnumerable<int>", null, @".+ImportReference\(typeof\(.+IEnumerable<>\)\)\.MakeGenericInstanceType\(.+Int32\)", IgnoreReason = "Placeholder")]
    public void Declaration(string paramsType, string? paramsAttribute, string actualCecilParameterType)
    {
            paramsAttribute ??= "ParamCollectionAttribute";
        var result = RunCecilifier($$"""
                                     using System.Collections.Generic;
                                     using System;

                                     M(1, 2, 3);

                                     void M(params {{paramsType}} items) { }
                                     """);
        
        Assert.That(
            result.GeneratedCode.ReadToEnd(), 
            Does.Match($"""
                         //M\(1, 2, 3\);
                         \s+var (?<mv>m_M_\d+) = new MethodDefinition\(".+g__M\|0_0", MethodAttributes.Private, assembly.MainModule.TypeSystem.Void\);
                         \s+var (?<pp>p_items_\d+) = new ParameterDefinition\("items", ParameterAttributes.None,{actualCecilParameterType}.?\);
                         \s+\k<pp>\.CustomAttributes\.Add\(new CustomAttribute\(.+{paramsAttribute}\)\.GetConstructor\(.+\)\)\)\);
                         \s+\k<mv>\.Parameters\.Add\(\k<pp>\);
                         """));
    }
}

static class Extensions
{
    public static string RegexEncoded(this string str) => Regex.Replace(str, @"(\[|\])", "\\$1");
}
