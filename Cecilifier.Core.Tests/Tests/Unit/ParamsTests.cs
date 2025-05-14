#nullable enable
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class ParamsTests : CecilifierUnitTestBase
{
    [TestCaseSource(nameof(CallSiteTestScenarios))]
    public void SimplestCallSite(string paramsType, params string[] expectedRegex)
    {
        var result = RunCecilifier($$"""
                                   using System.Collections.Generic;
                                   using System;
                                   
                                   class Foo 
                                   {
                                        void Use(int n) => M(1, 2, n);
                                        void M(params {{paramsType}} items) { }
                                   }
                                   """);
        
        var actual = result.GeneratedCode.ReadToEnd();
        foreach (var regex in expectedRegex)
        {
            Assert.That(actual, Does.Match(regex));
        }
    }
    
    [TestCase("long[]", """
                        (?<emit>\s+il_use_\d+\.Emit\(OpCodes\.)Ldc_I4, 2\);
                        \k<emit>Ldarg_1\);
                        \k<emit>Conv_I8\);
                        \k<emit>Stelem_I8\);
                        """)]
    
    [TestCase("Span<long>", """
                            (?<emit>\s+il_use_\d+\.Emit\(OpCodes\.)Ldc_I4, 2\);
                            \k<emit>Call, gi_inlineArrayElementRef_\d+\);
                            \k<emit>Ldarg_1\);
                            \k<emit>Conv_I8\);
                            \k<emit>Stind_I8\);
                            """)]
    
    [TestCase("IList<long>", """
                             (?<emit>\s+il_use_\d+\.Emit\(OpCodes\.)Call, m_getItem_\d+\);
                             \k<emit>Ldarg_1\);
                             \k<emit>Conv_I8\);
                             \k<emit>Stind_I8\);
                             """)]
    public void LongConversionAtCallSite(string paramsType, string expectedRegex)
    {
        var result = RunCecilifier($$"""
                                     using System.Collections.Generic;
                                     using System;

                                     class Foo 
                                     {
                                          void Use(int n) => M(1, 2, n);
                                          void M(params {{paramsType}} items) { }
                                     }
                                     """);
        
        var actual = result.GeneratedCode.ReadToEnd();
        Assert.That(actual, Does.Match(expectedRegex));
    }
    
    [TestCase("int[]", "ParamArrayAttribute", @".+assembly\.MainModule\.TypeSystem\.Int32\.MakeArrayType\(\)")]
    [TestCase("Span<int>", null, @".+ImportReference\(typeof\(.+Span<>\)\)\.MakeGenericInstanceType\(.+Int32\)")]
    [TestCase("ReadOnlySpan<int>", null, @".+ImportReference\(typeof\(.+ReadOnlySpan<>\)\)\.MakeGenericInstanceType\(.+Int32\)")]
    [TestCase("IList<int>", null, @".+ImportReference\(typeof\(.+IList<>\)\)\.MakeGenericInstanceType\(.+Int32\)")]
    [TestCase("ICollection<int>", null, @".+ImportReference\(typeof\(.+ICollection<>\)\)\.MakeGenericInstanceType\(.+Int32\)")]
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

    private const string NullableNotSupportedExtraHelp = " Nullable<T> (i.e, int?) are not supported yet.";
    private const string ParamsParameterTypeNotSupportedExtraHelp = " You may change items' to 'ICollection<T>'";
    [TestCase("IEnumerable<int>", ParamsParameterTypeNotSupportedExtraHelp)]
    [TestCase("IEnumerable<T>",  ParamsParameterTypeNotSupportedExtraHelp)]
    [TestCase("int?[]", NullableNotSupportedExtraHelp, TestName = "Nullable Array")]
    [TestCase("Span<int?>", NullableNotSupportedExtraHelp)]
    public void TestUnsupportedTypesAsParams(string paramsType, string expectedExtraHelp)
    {
        var result = RunCecilifier($$"""
                                   using System.Collections.Generic;
                                   using System;

                                   M(0, 1);

                                   void M<T>(T value, params {{paramsType}} items) { }
                                   """);

        Assert.That(result.Diagnostics.Count, Is.EqualTo(1));
        Assert.That(result.Diagnostics[0].Kind, Is.EqualTo(DiagnosticKind.Error));
        Assert.That(result.Diagnostics[0].Message, Does.Match($@"Cecilifier does not support type .+ as a 'params' parameter \(items\).{Regex.Escape(expectedExtraHelp)}"));
        Assert.That(result.GeneratedCode.ReadToEnd(), Contains.Substring(result.Diagnostics[0].Message));
    }
    
    static IEnumerable<TestCaseData> CallSiteTestScenarios()
    {
        yield return new TestCaseData(
            "int[]",
            new [] {
                """
                //M\(1, 2, n\)
                (?<emit>\s+il_use_\d+\.Emit\(OpCodes\.)Ldarg_0\);
                \s+var (?<paramsVar>l_itemsParams_\d+) = new VariableDefinition\(.+Int32.MakeArrayType\(\)\);
                \s+m_use_\d+.Body.Variables.Add\(\k<paramsVar>\);
                \k<emit>Ldc_I4, 3\);
                """
            }).SetName("int[]");
        
        yield return new TestCaseData(
            "Span<int>",
            new [] {
                """
                //M\(1, 2, n\)
                (?<emit>\s+il_use_\d+\.Emit\(OpCodes\.)Ldarg_0\);
                \s+//InlineArray to store the `params` values.
                """
            }).SetName("Span<int>");
        
        yield return new TestCaseData(
            "ReadOnlySpan<int>",
            new [] 
            {
                """
                //M\(1, 2, n\)
                (?<emit>\s+il_use_\d+\.Emit\(OpCodes\.)Ldarg_0\);
                \s+//InlineArray to store the `params` values.
                """,
                
                """
                var (?<ila>l_itemsArg_\d+) = new VariableDefinition\(st_inlineArray_\d+.MakeGenericInstanceType\(assembly.MainModule.TypeSystem.Int32\)\);
                \s+m_use_\d+.Body.Variables.Add\(\k<ila>\);
                (?<emit>\s+il_use_\d+\.Emit\(OpCodes\.)Ldloca_S, \k<ila>\);
                \k<emit>Initobj, st_inlineArray_\d+.MakeGenericInstanceType\(assembly.MainModule.TypeSystem.Int32\)\);
                \k<emit>Ldloca_S, \k<ila>\);
                \k<emit>Ldc_I4, 0\);
                """, // Declares and initialize a variable of an inline array type used to store the data.
                
                """
                (?<emit>\s+il_use_\d+\.Emit\(OpCodes\.)Call, gi_inlineArrayElementRef_\d+\);
                \k<emit>Ldc_I4, 1\);
                \k<emit>Stind_I4\);
                \k<emit>Ldloca_S, (?<ila>l_itemsArg_\d+)\);
                \k<emit>Ldc_I4, 1\);
                \k<emit>Call, gi_inlineArrayElementRef_\d+\);
                \k<emit>Ldc_I4, 2\);
                \k<emit>Stind_I4\);
                \k<emit>Ldloca_S, \k<ila>\);
                \k<emit>Ldc_I4, 2\);
                \k<emit>Call, gi_inlineArrayElementRef_\d+\);
                \k<emit>Ldarg_1\);
                \k<emit>Stind_I4\);
                """ // Populates inline array with data to be passed to params[]
            }).SetName("ReadOnlySpan<int>");
        
        var ilistTestData = new TestCaseData(
            "IList<int>",
            new []
            {
                """
                //M\(1, 2, n\)
                (?<emit>\s+il_use_\d+\.Emit\(OpCodes\.)Ldarg_0\);
                \s+//Instantiates a List<T> passing the # of elements to its ctor.
                \k<emit>Ldc_I4, 3\);
                \k<emit>Newobj, .+ImportReference\(.+List<System.Int32>\).+\);
                \k<emit>Dup\);
                """,
                """
                (?<emit>\s+il_use_\d+\.Emit\(OpCodes\.)Ldc_I4, 0\);
                \k<emit>Call, m_getItem_\d+\);
                \k<emit>Ldc_I4, 1\);
                \k<emit>Stind_I4\);
                \k<emit>Ldloca_S, l_listSpan_\d+\);
                \k<emit>Ldc_I4, 1\);
                \k<emit>Call, m_getItem_\d+\);
                \k<emit>Ldc_I4, 2\);
                \k<emit>Stind_I4\);
                \k<emit>Ldloca_S, l_listSpan_\d+\);
                \k<emit>Ldc_I4, 2\);
                \k<emit>Call, m_getItem_\d+\);
                \k<emit>Ldarg_1\);
                \k<emit>Stind_I4\);
                \k<emit>Call, m_M_\d+\);
                """
            }).SetName("IList<int>");

        yield return ilistTestData;
        
        yield return new TestCaseData(
            "ICollection<int>",
            (string[]) ilistTestData.Arguments[1]!) // call site code generated for ICollection<T> is identical to the code generated for IList<T>.
            .SetName("ICollection<int>");
        
        yield return new TestCaseData(
            "ReadOnlySpan<int>",
            new [] 
            {
                """
                //M\(1, 2, n\)
                (?<emit>\s+il_use_\d+\.Emit\(OpCodes\.)Ldarg_0\);
                \s+//InlineArray to store the `params` values.
                """,
                
                """
                var (?<ila>l_itemsArg_\d+) = new VariableDefinition\(st_inlineArray_\d+.MakeGenericInstanceType\(assembly.MainModule.TypeSystem.Int32\)\);
                \s+m_use_\d+.Body.Variables.Add\(\k<ila>\);
                (?<emit>\s+il_use_\d+\.Emit\(OpCodes\.)Ldloca_S, \k<ila>\);
                \k<emit>Initobj, st_inlineArray_\d+.MakeGenericInstanceType\(assembly.MainModule.TypeSystem.Int32\)\);
                \k<emit>Ldloca_S, \k<ila>\);
                \k<emit>Ldc_I4, 0\);
                """, // Declares and initialize a variable of an inline array type used to store the data.
                
                """
                (?<emit>\s+il_use_\d+\.Emit\(OpCodes\.)Call, gi_inlineArrayElementRef_\d+\);
                \k<emit>Ldc_I4, 1\);
                \k<emit>Stind_I4\);
                \k<emit>Ldloca_S, (?<ila>l_itemsArg_\d+)\);
                \k<emit>Ldc_I4, 1\);
                \k<emit>Call, gi_inlineArrayElementRef_\d+\);
                \k<emit>Ldc_I4, 2\);
                \k<emit>Stind_I4\);
                \k<emit>Ldloca_S, \k<ila>\);
                \k<emit>Ldc_I4, 2\);
                \k<emit>Call, gi_inlineArrayElementRef_\d+\);
                \k<emit>Ldarg_1\);
                \k<emit>Stind_I4\);
                """ // Populates inline array with data to be passed to params[]
            }).SetName("ReadOnlySpan<int>");
    }
}
