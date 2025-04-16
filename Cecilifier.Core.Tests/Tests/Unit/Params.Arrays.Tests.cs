using System.Text.RegularExpressions;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class ParamsArraysTests : CecilifierUnitTestBase
{
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
