using System.Linq;
using Cecilifier.Core.CodeGeneration;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
        
        var _ = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(context, 10, "int", "123");
        found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Type);
        Assert.That(found.Count(), Is.EqualTo(2), "2 types should have been generated. PrivateImplementationDetails and a second one, used to store the raw data"); 
        
        // run a second time... simulating a second array initialization being processed.
        _ = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(context, 10,"int","123");
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
        
        var _ = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(context, 4, "int", "0123");
        found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Type);
        Assert.That(found.Count(), Is.EqualTo(1));
        Assert.That(context.Output, Does.Match(@"var fld_arrayInitializerData_1 = new FieldDefinition\(.+assembly.MainModule.TypeSystem.Int32\);"));
        
        // run a second time... simulating a second array initialization being processed.
        _ = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(context, 8,"int","012345678");
        found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Type);
        Assert.That(found.Count(), Is.EqualTo(1));
        
        Assert.That(context.Output, Does.Match(@"var fld_arrayInitializerData_2 = new FieldDefinition\(.+assembly.MainModule.TypeSystem.Int64\);"));
    }
    
    [Test]
    public void BackingField_ForSameSize_IsCached()
    {
        var comp = CompilationFor("class Foo {}");        
        var context = new CecilifierContext(comp.GetSemanticModel(comp.SyntaxTrees[0]), new CecilifierOptions(), 1);

        var found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Field);
        Assert.That(found.Any(), Is.False);
        
        var variableName = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(context, 10, "int", "123");
        found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Field);
        Assert.That(found.Count(), Is.EqualTo(1));
        
        // run a second time... simulating a second array initialization with same size being processed.
        var secondVariableName = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(context, 10, "int","123");
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
        
        var variableName = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(context, 12, "int","{1, 2, 3, 4}");
        found = context.DefinitionVariables.GetVariablesOf(VariableMemberKind.Field);
        Assert.That(found.Count(), Is.EqualTo(1));
        
        // run a second time... simulating a second array initialization with a different size being processed.
        var secondVariableName = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(context, 20, "int","{1, 2 , 3, 4, 5}");
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
        
        Assert.That(context.Output, Does.Match("""var m_inlineArrayAsSpan_\d+ = new MethodDefinition\("InlineArrayAsSpan", MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig, assembly.MainModule.TypeSystem.Void\);"""));
        Assert.That(context.Output, Does.Match("""m_inlineArrayAsSpan_\d+.Parameters.Add\(new ParameterDefinition\("buffer", ParameterAttributes.None, gp_tBuffer_\d+.MakeByReferenceType\(\)\)\);"""));
        Assert.That(context.Output, Does.Match("""m_inlineArrayAsSpan_\d+.Parameters.Add\(new ParameterDefinition\("length", ParameterAttributes.None, assembly.MainModule.TypeSystem.Int32\)\);"""));
    }
    
    static CSharpCompilation CompilationFor(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        return CSharpCompilation.Create("Test", new[] { syntaxTree }, references: new [] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location), });
    }
}
