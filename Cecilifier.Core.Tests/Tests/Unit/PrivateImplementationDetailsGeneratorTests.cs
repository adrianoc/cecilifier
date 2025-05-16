using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using Cecilifier.Core.CodeGeneration;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public class PrivateImplementationDetailsGeneratorTests
{
    [Test]
    public void PrivateImplementationType_IsCached()
    {
        var comp = CompilationFor("class Foo {}");
        var context = new CecilifierContext(comp.GetSemanticModel(comp.SyntaxTrees[0]), new CecilifierOptions(), 1);

        var found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Type);
        Assert.That(found.Any(), Is.False);
        
        _ = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(context, sizeof(int), ["1", "2", "3"], StringToSpanOfBytesConverters.Int32);
        found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Type);
        Assert.That(found.Count(), Is.EqualTo(2), "2 types should have been generated. PrivateImplementationDetails and a second one, used to store the raw data"); 
        
        // run a second time... simulating a second array initialization being processed.
        _ = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(context, sizeof(int), ["1", "2", "3"], StringToSpanOfBytesConverters.Int32);
        found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Type);
        Assert.That(found.Count(), Is.EqualTo(2));
    }
    
    [Test]
    public void Int32AndInt64_AreUsedAsFieldBackingType_OfArraysOf4And8Bytes()
    {
        var comp = CompilationFor("class Foo {}");
        var context = new CecilifierContext(comp.GetSemanticModel(comp.SyntaxTrees[0]), new CecilifierOptions(), 1);

        var found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Type);
        Assert.That(found.Any(), Is.False);
        
        _ = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(context, sizeof(byte), ["1", "2", "3", "4"], StringToSpanOfBytesConverters.Byte);
        found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Type);
        Assert.That(found.Count(), Is.EqualTo(1));
        Assert.That(context.Output, Does.Match(@"var fld_arrayInitializerData_1 = new FieldDefinition\(.+assembly.MainModule.TypeSystem.Int32\);"));
        
        // run a second time... simulating a second array initialization being processed.
        _ = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(context, sizeof(short), ["1", "2", "3", "4"], StringToSpanOfBytesConverters.Int16);
        found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Type);
        Assert.That(found.Count(), Is.EqualTo(1));
        
        Assert.That(context.Output, Does.Match(@"var fld_arrayInitializerData_2 = new FieldDefinition\(.+assembly.MainModule.TypeSystem.Int64\);"));
    }
    
    [TestCaseSource(nameof(BackingFieldNameTestScenarios))]
    public void BackingFieldName_Matches_CSharpCompilerNaming(string array, int elementSize, string expectedFieldName)
    {
        // If this test every fail it has a high chance that a new version of Roslyn has changed the way the field name is computed.
        // This test assumes the implementation from: https://github.com/dotnet/roslyn/blob/b7e891b8a884be1519a709edc7121140c5a1fac2/src/Compilers/Core/Portable/CodeGen/PrivateImplementationDetails.cs#L209
        var comp = CompilationFor($"class Foo {{ {array} }}");
        var context = new CecilifierContext(comp.GetSemanticModel(comp.SyntaxTrees[0]), new CecilifierOptions(), 1);

        var found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Field).SingleOrDefault();
        Assert.That(found, Is.Null);
        
        var arrayDeclaration = comp.SyntaxTrees[0].GetRoot().DescendantNodes().OfType<VariableDeclarationSyntax>().Single();
        var stringToByteSpanConverter = StringToSpanOfBytesConverters.For(arrayDeclaration.Type.NameFrom());
        var elements = comp.SyntaxTrees[0].GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>().Select(exp => exp.ValueText()).ToArray();

        PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(context, elementSize, elements, stringToByteSpanConverter);
        found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Field).SingleOrDefault();
        Assert.That(found, Is.Not.Null);
        
        Assert.That(found.MemberName, Is.EqualTo(expectedFieldName), array);
    }
    
    [Test]
    public void BackingField_ForSameSize_IsCached()
    {
        var comp = CompilationFor("class Foo {}");
        var context = new CecilifierContext(comp.GetSemanticModel(comp.SyntaxTrees[0]), new CecilifierOptions(), 1);

        var found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Field);
        Assert.That(found.Any(), Is.False);
        
        var variableName = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(context, sizeof(int), ["1", "2", "3"], StringToSpanOfBytesConverters.Int32);
        found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Field);
        Assert.That(found.Count(), Is.EqualTo(1));
        
        // run a second time... simulating a second array initialization with same size being processed.
        var secondVariableName = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(context, sizeof(int), ["1", "2", "3"], StringToSpanOfBytesConverters.Int32);
        found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Field);
        
        Assert.That(found.Count(), Is.EqualTo(1));
        Assert.That(secondVariableName, Is.EqualTo(variableName));
    }
    
    [Test]
    public void BackingField_IsUniquePerDataSize()
    {
        var comp = CompilationFor("class Foo {}");
        var context = new CecilifierContext(comp.GetSemanticModel(comp.SyntaxTrees[0]), new CecilifierOptions(), 1);

        var found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Field);
        Assert.That(found.Any(), Is.False);
        
        var variableName = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(context, sizeof(int), ["1", "2", "3"], StringToSpanOfBytesConverters.Int32);
        found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Field);
        Assert.That(found.Count(), Is.EqualTo(1));
        
        // run a second time... simulating a second array initialization with a different size being processed.
        var secondVariableName = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(context, sizeof(int), ["1", "2", "3", "4"], StringToSpanOfBytesConverters.Int32);
        found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Field);
        
        Assert.That(found.Count(), Is.EqualTo(2), context.Output);
        Assert.That(secondVariableName, Is.Not.EqualTo(variableName), context.Output);
    }

    [Test]
    public void InlineArrayAsSpan_HelperMethod_Properties()
    {
        var comp = CompilationFor("class Foo {}");
        var context = new CecilifierContext(comp.GetSemanticModel(comp.SyntaxTrees[0]), new CecilifierOptions(), 1);

        var found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Method).ToArray();
        Assert.That(found.Length, Is.EqualTo(0));
        
        // internal static Span<TElement> InlineArrayAsSpan<TBuffer, TElement>(ref TBuffer buffer, int length)
        var methodVariableName = PrivateImplementationDetailsGenerator.GetOrEmmitInlineArrayAsSpanMethod(context);
        found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Method).ToArray();
        Assert.That(found.Length, Is.EqualTo(1));
        Assert.That(found[0].MemberName, Is.EqualTo("InlineArrayAsSpan"));

        Assert.That(context.Output,
            Does.Match(
                """var m_inlineArrayAsSpan_\d+ = new MethodDefinition\("InlineArrayAsSpan", MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig, assembly.MainModule.TypeSystem.Void\);"""));
        Assert.That(context.Output, Does.Match("""var p_buffer_\d+ = new ParameterDefinition\("buffer", ParameterAttributes.None, gp_tBuffer_\d+.MakeByReferenceType\(\)\);"""));
        Assert.That(context.Output, Does.Match("""m_inlineArrayAsSpan_\d+.Parameters.Add\(p_buffer_\d+\);"""));
        Assert.That(context.Output, Does.Match("""var p_length_\d+ = new ParameterDefinition\("length", ParameterAttributes.None, assembly.MainModule.TypeSystem.Int32\);"""));
        Assert.That(context.Output, Does.Match("""m_inlineArrayAsSpan_\d+.Parameters.Add\(p_length_\d+\);"""));
    }
    
    static CSharpCompilation CompilationFor(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        return CSharpCompilation.Create("Test", new[] { syntaxTree }, references: new [] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location), });
    }

    static TestCaseData[] BackingFieldNameTestScenarios()
    {
        var actualPrivateImplementationDetails = Type.GetType("<PrivateImplementationDetails>")!;
        var fields = actualPrivateImplementationDetails.GetFields(BindingFlags.NonPublic | BindingFlags.Static);
        return [
            TestCaseDataFor(() => ArraysForOptimizedFieldNameTests.Int32_12, fields),
            TestCaseDataFor(() => ArraysForOptimizedFieldNameTests.Int32_16, fields),
            TestCaseDataFor(() => ArraysForOptimizedFieldNameTests.Int64_24, fields),
            TestCaseDataFor(() => ArraysForOptimizedFieldNameTests.Byte_6, fields),
            TestCaseDataFor(() => ArraysForOptimizedFieldNameTests.Boolean_5, fields),
            TestCaseDataFor(() => ArraysForOptimizedFieldNameTests.Char_10, fields)
        ];

        static TestCaseData TestCaseDataFor<T>(Expression<Func<T[]>> expression, FieldInfo[] fields)
        {
            var fieldExpression = (MemberExpression) expression.Body;
            var fieldName = fieldExpression.Member.Name.AsSpan();
            var sizeSeparatorIndex = fieldName.IndexOf('_');
            var sizeSpan = fieldName.Slice(sizeSeparatorIndex + 1);
            var fieldTypeName = $"__StaticArrayInitTypeSize={Int32.Parse(sizeSpan)}";
            
            var x = expression.Compile();
            var arrayValues = x();
            
            return new TestCaseData(
                            $"{arrayValues[0].GetType().FullName}[] _array = [ {string.Join(',', arrayValues.Select(item => item.ToString()!.ToLower()))}]", // Array
                            // Marshal.SizeOf() returns the unmanaged size of the type which does not match for System.Char / System.Boolean
                            arrayValues[0].GetType().FullName switch
                            {
                                "System.Char" => sizeof(char),
                                "System.Boolean" => sizeof(bool),
                                _ => Marshal.SizeOf(arrayValues[0].GetType())
                            },
                            fields.Single(f => f.FieldType.Name == fieldTypeName).Name // expected field name
                            ).SetName($"{arrayValues.Length} {arrayValues[0].GetType().Name}");
        }
    }
    
    static class ArraysForOptimizedFieldNameTests
    {
        public static int[] Int32_12 = [2, 4, 6];                           // 3 * sizeof(int)
        public static int[] Int32_16 = [2, 4, 6, 8];                        // 4 * sizeof(int)
        public static long[] Int64_24 = [42, 314, 5];                       // 3 * sizeof(long)
        public static byte[] Byte_6 = [1, 2, 3, 4, 5, 6];                   // 6 * sizeof(byte)
        public static bool[] Boolean_5 = [true, true, true, false, false];  // 5 * sizeof(bool); We need at least 5 bools for the optimization to kick in and add the extra type. 
        public static char[] Char_10 = ['1','2','3','4','5'];               // 5 * sizeof(char)
    }
}
