using Microsoft.CodeAnalysis.CSharp;

#nullable enable
namespace Cecilifier.Core.AST.MemberDependencies;

internal interface IMemberDependencyFactory<out T> where T : MemberDependency
{
    public static abstract T CreateInstance(CSharpSyntaxNode? syntaxNode);
}
