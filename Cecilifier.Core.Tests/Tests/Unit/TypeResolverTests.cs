using System.Collections.Generic;
using System.Linq;
using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

#nullable enable

[TestFixture]
internal class TypeResolverTests : CecilifierContextBasedTestBase<MonoCecilContext>
{
    protected override string Snippet =>
        """
        using System;
        using System.Collections.Generic;
        using Cecilifier.Core.Tests.Tests.Unit;

        class Foo<T> 
        { 
            Func<T> M1() => default;
            Func<TM> M2<TM>() => default; 
            Func<T, TM> M3<TM>() => default;
            
            List<int> M4<TM>(List<TM> list) => list.ConvertAll(GenToInt<TM>); 
            
            static int GenToInt<TGen>(TGen gen) => 1;
            
            List<Bar>.Enumerator Bars() => new();
            
            A<Bar, int>.B<Bar> B() => default;
            A<Bar, int>.B<Bar>.C C() => default;
            D.E<Bar> E() => default;
            D.F F() => default;
        }

        class Bar {}
        """;

    protected override IEnumerable<MetadataReference> ExtraAssemblyReferences()  => [MetadataReference.CreateFromFile(typeof(TypeResolverTests).Assembly.Location)];

    [Test]
    public void TypeParameterFromDeclaringType_Resolves_To_DeclaredVariable()
    {
        var context = NewContext();
        var m1Syntax = GetMethodSyntax(context, "M1"); // Func<T> M1() {}
        var m1Symbol = context.SemanticModel.GetDeclaredSymbol(m1Syntax).EnsureNotNull();

        // Simulates type parameter `T` being registered under type `Foo`
        using var _ = context.DefinitionVariables.WithCurrent("Foo<T>", "T", VariableMemberKind.TypeParameter, "TypeParameter_T_var");
        var resolved = context.TypeResolver.ResolveAny(m1Symbol.ReturnType, "fakeReference"); 
        
        Assert.That(resolved, Does.Match(@".+ImportReference\(typeof\(System.Func<>\)\)\.MakeGenericInstanceType\(TypeParameter_T_var\)"));
    }

    [Test]
    public void TypeParameterFromGenericMethod_Resolves_To_DeclaredVariable()
    {
        var context = NewContext();
        var methodSyntax = GetMethodSyntax(context, "M2"); // Func<TM> M2<TM>() {}
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodSyntax).EnsureNotNull();

        // Simulates type parameter `T` being registered under method `M2`
        using var _ = context.DefinitionVariables.WithCurrent("Foo<T>.M2<TM>()", "TM", VariableMemberKind.TypeParameter, "TypeParameter_TM_var");
        var resolved = context.TypeResolver.ResolveAny(methodSymbol.OriginalDefinition.ReturnType, "fakeReference"); 
        
        Assert.That(resolved, Does.Match(@".+ImportReference\(typeof\(System.Func<>\)\)\.MakeGenericInstanceType\(TypeParameter_TM_var\)"));
    }    
    
    [Test]
    public void TypeParameterFromGenericMethodAndGenericType_Resolves_To_DeclaredVariables()
    {
        var context = NewContext();
        var methodSyntax = GetMethodSyntax(context, "M3"); // Func<T, TM> M3<TM>() {}
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodSyntax).EnsureNotNull();

        // Simulates type parameters `T` & `TM` being registered under their respective members.
        using var t = context.DefinitionVariables.WithCurrent("Foo<T>", "T", VariableMemberKind.TypeParameter, "TypeParameter_Foo");
        using var tm = context.DefinitionVariables.WithCurrent("Foo<T>.M3<TM>()", "TM", VariableMemberKind.TypeParameter, "TypeParameter_M3");
        var resolved = context.TypeResolver.ResolveAny(methodSymbol.OriginalDefinition.ReturnType, "fakeReference"); 
        
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
        var resolved = context.TypeResolver.ResolveAny(methodSymbol.OriginalDefinition.ReturnType, "methodReference");
        Assert.That(resolved, Does.Match(@".+ImportReference\(typeof\(System.Collections.Generic.List<>\)\)\.MakeGenericInstanceType\(methodReference.GenericParameters\[0\]\)"));
    }
    
    [TestCase("B", 
        """.+NewRawNestedTypeReference\("B", .+, .+ImportReference\(typeof\(Cecilifier.Core.Tests.Tests.Unit.A<,>\)\), isValueType: false, 3\).MakeGenericInstanceType\(BarDefinition, assembly.MainModule.TypeSystem.Int32, BarDefinition\)""",
        TestName = "Generic nested and parent type")]
    [TestCase("C", 
        """.+NewRawNestedTypeReference\("C", .+, .+NewRawNestedTypeReference\("B", .+, .+ImportReference\(typeof\(Cecilifier.Core.Tests.Tests.Unit.A<,>\)\), isValueType: false, 3\), isValueType: false, 3\).MakeGenericInstanceType\(BarDefinition, assembly.MainModule.TypeSystem.Int32, BarDefinition\)""",
        TestName = "Non generic nested type with generic parents")]
    [TestCase("E", 
        """.+NewRawNestedTypeReference\("E", .+, .+ImportReference\(typeof\(Cecilifier.Core.Tests.Tests.Unit.D\)\), isValueType: false, 1\).MakeGenericInstanceType\(BarDefinition\)""",
        TestName = "Generic nested type of non generic parent")]
    public void NestedTypes_WithTypeArguments_DefinedInCecilifiedCode(string methodName, string expectedTypeReference)
    {
        // A<Bar, int>.B<Bar>.C C() => default;
        // D.E<Bar> E() => default;

        var context = NewContext();
        var methodSyntax = GetMethodSyntax(context, methodName);
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodSyntax).EnsureNotNull<ISymbol, IMethodSymbol>();

        using var bar = context.DefinitionVariables.WithCurrent("<global namespace>", "Bar", VariableMemberKind.Type, "BarDefinition");
        var resolved = context.TypeResolver.ResolveAny(methodSymbol.OriginalDefinition.ReturnType, "methodReference");
        Assert.That(
            resolved, 
            Does.Match(expectedTypeReference));
    }
    
    [Test]
    public void NonGeneric_NestedTypes()
    {
        var context = NewContext();
        var methodSyntax = GetMethodSyntax(context, "F");
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodSyntax).EnsureNotNull<ISymbol, IMethodSymbol>();
        var resolved = context.TypeResolver.ResolveAny(methodSymbol.OriginalDefinition.ReturnType, "methodReference");
        Assert.That(
            resolved, 
            Does.Match("""assembly.MainModule.ImportReference\(typeof\(Cecilifier.Core.Tests.Tests.Unit.D.F\)\)"""));
    }
}

public class A<T1, T2>
{
    public class B<T3>
    {
        public class C { }
    }
}

public class D
{
    public class E<T> { }
    
    public class F {}
}
