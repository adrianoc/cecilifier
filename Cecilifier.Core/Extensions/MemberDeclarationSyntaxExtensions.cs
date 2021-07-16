using System;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Extensions
{
    public static class MemberDeclarationSyntaxExtensions
    {
        public static string Name(this MemberDeclarationSyntax node)
        {
            return node switch
            {
                DelegateDeclarationSyntax del => del.Identifier.Text,
                BaseTypeDeclarationSyntax bt => bt.Identifier.Text,
                PropertyDeclarationSyntax prop => prop.Identifier.Text,
                IndexerDeclarationSyntax indexer => "indexer",
                BaseFieldDeclarationSyntax field => field.Declaration.Variables.First().Identifier.Text,
                MethodDeclarationSyntax method => method.Identifier.Text,
                EventDeclarationSyntax @event => @event.Identifier.Text,
                EnumMemberDeclarationSyntax enumMember => enumMember.Identifier.Text,
                _ => throw new Exception($"{node.GetType().Name} ({node}) is not supported")
            };
        }
    }
}
