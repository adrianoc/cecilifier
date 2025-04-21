using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class ParamsArraysTests : CecilifierUnitTestBase
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
        
        Assert.That(
            result.GeneratedCode.ReadToEnd(), 
            Does.Match("""
                       //M\(1, 2, 3\)
                       (?<emit>\s+il_use_\d+\.Emit\(OpCodes\.)Ldarg_0\);
                       \s+var (?<itemsParams>l_itemsParams_\d+) = new VariableDefinition\(.+Int32\.MakeArrayType\(\)\);
                       \s+m_use_\d+.Body.Variables.Add\(\k<itemsParams>\);
                       \k<emit>Ldc_I4, 3\);
                       \k<emit>Newarr, .+Int32\);
                       \k<emit>Stloc, \k<itemsParams>\);
                       \k<emit>Ldloc, \k<itemsParams>\);
                       \k<emit>Dup\);
                       \k<emit>Ldc_I4, 0\);
                       \k<emit>Ldc_I4, 1\);
                       \k<emit>Stelem_I4\);
                       \k<emit>Dup\);
                       \k<emit>Ldc_I4, 1\);
                       \k<emit>Ldc_I4, 2\);
                       \k<emit>Stelem_I4\);
                       \k<emit>Dup\);
                       \k<emit>Ldc_I4, 2\);
                       \k<emit>Ldc_I4, 3\);
                       \k<emit>Stelem_I4\);
                       \k<emit>Call, m_M_\d+\);
                       \k<emit>Ret\);
                       """));
    }
    
    static IEnumerable<TestCaseData> CallSiteTestScenarios()
    {
        yield return new TestCaseData(
            "int[]",
            """
            //M\(1, 2, 3\)
            (?<emit>\s+il_use_\d+\.Emit\(OpCodes\.)Ldarg_0\);
            \k<emit>Ldc_I4, 3\);
            \k<emit>NewArray, .+Int32.+\);
            \k<emit>Dup\);
            \k<emit>Ldc_I4, 0\);
            \k<emit>Ldc_I4, 1\);
            \k<emit>StElem_I4\);
            \k<emit>Dup\);
            \k<emit>Ldc_I4, 1\);
            \k<emit>Ldc_I4, 2\);
            \k<emit>StElem_I4\);
            \k<emit>Dup\);
            \k<emit>Ldc_I4, 2\);
            \k<emit>Ldc_I4, 3\);
            \k<emit>StElem_I4\);
            \k<emit>Call, m_M_\d+\);
            \k<emit>Ret\);
            """).SetName("int[]");
        
        yield return new TestCaseData(
            "Span<int>",
            """
            
            """).SetName("Span<int>").Ignore("placeholder");
        
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

    [TestCase("int[]", @".+assembly\.MainModule\.TypeSystem\.Int32\.MakeArrayType\(\)")]
    [TestCase("Span<int>", @".+ImportReference\(typeof\(.+Span<>\)\)\.MakeGenericInstanceType\(.+Int32\)")]
    [TestCase("ReadOnlySpan<int>", @".+ImportReference\(typeof\(.+ReadOnlySpan<>\)\)\.MakeGenericInstanceType\(.+Int32\)")]
    [TestCase("IList<int>", @".+ImportReference\(typeof\(.+IList<>\)\)\.MakeGenericInstanceType\(.+Int32\)")]
    [TestCase("ICollection<int>", @".+ImportReference\(typeof\(.+ICollection<>\)\)\.MakeGenericInstanceType\(.+Int32\)")]
    [TestCase("IEnumerable<int>", @".+ImportReference\(typeof\(.+IEnumerable<>\)\)\.MakeGenericInstanceType\(.+Int32\)")]
    public void Declaration(string paramsType, string actualCecilParameterType)
    {
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
                         \s+\k<pp>\.CustomAttributes\.Add\(new CustomAttribute\(.+ParamArrayAttribute\)\.GetConstructor\(.+\)\)\)\);
                         \s+\k<mv>\.Parameters\.Add\(\k<pp>\);
                         """));
    }
}


static class Extensions
{
    public static string RegexEncoded(this string str) => Regex.Replace(str, @"(\[|\])", "\\$1");
}
