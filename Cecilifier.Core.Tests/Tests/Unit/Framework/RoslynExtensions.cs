using System;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Tests.Tests.Unit.Framework;

public static class RoslynExtensions
{
    internal static string MemberName(this CSharpSyntaxNode syntaxNode)
    {
        var dependeeName = syntaxNode switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            EventDeclarationSyntax e => e.Identifier.Text,
            EventFieldDeclarationSyntax ef => ef.Declaration.Variables[0].Identifier.Text,
            FieldDeclarationSyntax f => f.Declaration.Variables[0].Identifier.Text,
            VariableDeclaratorSyntax v => v.Identifier.Text,
            ConstructorDeclarationSyntax c=> c.Identifier.Text,
            OperatorDeclarationSyntax op => op.OperatorToken.ToString(),
            ConversionOperatorDeclarationSyntax cc => cc.Type.ToString(),
            _ => throw new InvalidOperationException($"Dependency type {syntaxNode.GetType()} is not supported ({syntaxNode}).")
        };
        return dependeeName;
    }
}
