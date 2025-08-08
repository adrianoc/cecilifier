using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit.Framework;

internal abstract class CecilifierContextBasedTestBase
{
    protected abstract string Snippet { get;  }
    protected virtual IEnumerable<MetadataReference> ExtraAssemblyReferences() => [];
    
    private CSharpCompilation _comp;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(Snippet);
        
        _comp = CSharpCompilation.Create(
            "TypeResolverTests",
            [syntaxTree],
            [
                ..ExtraAssemblyReferences(),
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var diagnostics = _comp.GetDiagnostics();
        if (diagnostics.Any())
        {
            throw new InvalidOperationException(diagnostics.Aggregate("", (acc, diag) => $"{acc}\n{diag}"));
        }
    }

    protected MethodDeclarationSyntax GetMethodSyntax(CecilifierContext context, string methodName)
    {
        return context.SemanticModel.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == methodName);
    }
    
    //TODO: Is this test ILApiDriver specific ? for now only test for Mono.Cecil.
    protected CecilifierContext NewContext() => new(_comp.GetSemanticModel(_comp.SyntaxTrees[0]), new CecilifierOptions {GeneratorApiDriver = new MonoCecilGeneratorDriver() }, 1);
}
