using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core.AST.MemberDependencies;
using Cecilifier.Core.Tests.Tests.Unit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

public class MemberDependencyTestBase
{
    private protected IReadOnlyCollection<MemberDependency> CollectDependenciesFromSingleType(CSharpCompilation compilation)
    {
        var collector = new MemberDependencyCollector<MemberDependency>();

        var typeUnderTest = compilation.SyntaxTrees[0].GetRoot().ChildNodes().OfType<TypeDeclarationSyntax>().Single();
        var computedDependencies = collector.Process(typeUnderTest, compilation.GetSemanticModel(compilation.SyntaxTrees[0]));
        return computedDependencies;
    }
    
    protected static CSharpCompilation CompilationFor(params string[] code)
    {
        var syntaxTrees = code.Select(source => CSharpSyntaxTree.ParseText(source));
        var comp = CSharpCompilation.Create("Test", syntaxTrees, Basic.Reference.Assemblies.Net80.References.All, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        
        var errors = comp.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())
            .ToArray();
        
        if (errors.Any())
            throw new ArgumentException($"Code has compilation errors:\n\t{string.Join("\n\t", errors)}");
        return comp;
    }

    internal static string MemberNameFrom(MemberDependency dependency) => dependency.Declaration.MemberName();
}
