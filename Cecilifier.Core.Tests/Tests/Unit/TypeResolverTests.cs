using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

#nullable enable

[TestFixture]
public class TypeResolverTests
{
    private CSharpCompilation _comp;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("""
                                                    using System;
                                                    using System.Collections.Generic;
                                                    class Foo<T> 
                                                    { 
                                                        Func<T> M1() => default;
                                                        Func<TM> M2<TM>() => default; 
                                                        Func<T, TM> M3<TM>() => default;
                                                        
                                                        List<int> M4<TM>(List<TM> list) => list.ConvertAll(GenToInt<TM>); 
                                                        
                                                        static int GenToInt<TGen>(TGen gen) => 1;
                                                    }
                                                    """);
        _comp = CSharpCompilation.Create(
            "TypeResolverTests", 
            new[] { syntaxTree },
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            ]
        );
    }
    
    [Test]
    public void TypeParameterFromDeclaringType_Resolves_To_DeclaredVariable()
    {
        var context = NewContext();
        var m1Syntax = GetMethodSyntax(context, "M1"); // Func<T> M1() {}
        var m1Symbol = context.SemanticModel.GetDeclaredSymbol(m1Syntax);

        // Simulates type parameter `T` being registered under type `Foo`
        using var _ = context.DefinitionVariables.WithCurrent("Foo", "T", VariableMemberKind.TypeParameter, "TypeParameter_T_var");
        var resolved = context.TypeResolver.Resolve(m1Symbol.ReturnType, "fakeReference"); 
        
        Assert.That(resolved, Does.Match(@".+ImportReference\(typeof\(System.Func<>\)\)\.MakeGenericInstanceType\(TypeParameter_T_var\)"));
    }

    [Test]
    public void TypeParameterFromGenericMethod_Resolves_To_DeclaredVariable()
    {
        var context = NewContext();
        var methodSyntax = GetMethodSyntax(context, "M2"); // Func<TM> M2<TM>() {}
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodSyntax);

        // Simulates type parameter `T` being registered under method `M2`
        using var _ = context.DefinitionVariables.WithCurrent("M2", "TM", VariableMemberKind.TypeParameter, "TypeParameter_TM_var");
        var resolved = context.TypeResolver.Resolve(methodSymbol.OriginalDefinition.ReturnType, "fakeReference"); 
        
        Assert.That(resolved, Does.Match(@".+ImportReference\(typeof\(System.Func<>\)\)\.MakeGenericInstanceType\(TypeParameter_TM_var\)"));
    }    
    
    [Test]
    public void TypeParameterFromGenericMethodAndGenericType_Resolves_To_DeclaredVariables()
    {
        var context = NewContext();
        var methodSyntax = GetMethodSyntax(context, "M3"); // Func<T, TM> M3<TM>() {}
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodSyntax);

        // Simulates type parameters `T` & `TM` being registered under their respective members.
        using var t = context.DefinitionVariables.WithCurrent("Foo", "T", VariableMemberKind.TypeParameter, "TypeParameter_Foo");
        using var tm = context.DefinitionVariables.WithCurrent("M3", "TM", VariableMemberKind.TypeParameter, "TypeParameter_M3");
        var resolved = context.TypeResolver.Resolve(methodSymbol.OriginalDefinition.ReturnType, "fakeReference"); 
        
        Assert.That(resolved, Does.Match(@".+ImportReference\(typeof\(System.Func<,>\)\)\.MakeGenericInstanceType\(TypeParameter_Foo, TypeParameter_M3\)"));
    }

    [Test]
    public void TypeParameterFromGenericMethodUsedAsTypeArgumentOfMethodReturn_Resolves_To_MethodTypeParameter()
    {
        var context = NewContext();
        var methodSyntax = GetMethodSyntax(context, "M4"); // List<int> M4<TM>(List<TM> list) => list.ConvertAll(GenToInt<TM>);
        var convertAllInvocation = methodSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>().SingleOrDefault()!;
        var methodSymbol = context.SemanticModel.GetSymbolInfo(convertAllInvocation.Expression).Symbol.EnsureNotNull<ISymbol, IMethodSymbol>();

        // Check the return type of `ConvertAll()` invocation
        var resolved = context.TypeResolver.Resolve(methodSymbol.OriginalDefinition.ReturnType, "methodReference");
        Assert.That(resolved, Does.Match(@".+ImportReference\(typeof\(System.Collections.Generic.List<>\)\)\.MakeGenericInstanceType\(methodReference.GenericParameters\[0\]\)"));
    }

    private MethodDeclarationSyntax GetMethodSyntax(CecilifierContext context, string methodName)
    {
        return context.SemanticModel.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == methodName);
    }
    
    private CecilifierContext NewContext() => new(_comp.GetSemanticModel(_comp.SyntaxTrees[0]), new CecilifierOptions(), 1);
}
