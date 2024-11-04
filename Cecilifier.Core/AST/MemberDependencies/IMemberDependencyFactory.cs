using Microsoft.CodeAnalysis.CSharp;

namespace Cecilifier.Core.AST.MemberDependencies;

internal interface IMemberDependencyFactory<T> where T : MemberDependency
{
    public static abstract T CreateInstance(CSharpSyntaxNode syntaxNode);
}
