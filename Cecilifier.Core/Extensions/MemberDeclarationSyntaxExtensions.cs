using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
                IndexerDeclarationSyntax => "indexer",
                BaseFieldDeclarationSyntax field => field.Declaration.Variables.First().Identifier.Text,
                MethodDeclarationSyntax method => method.Identifier.Text,
                EventDeclarationSyntax @event => @event.Identifier.Text,
                EnumMemberDeclarationSyntax enumMember => enumMember.Identifier.Text,
                ConversionOperatorDeclarationSyntax conversionOperator => conversionOperator.ImplicitOrExplicitKeyword.IsKind(SyntaxKind.ExplicitKeyword) ? "op_Explicit" : "op_Implicit",
                OperatorDeclarationSyntax @operator => @operator.ParameterList.Parameters.Count == 1 ? UnaryOperatorNameFrom(@operator.OperatorToken.Kind()) : BinaryOperatorNameFrom(@operator.OperatorToken.Kind()),
                _ => throw new Exception($"{node.GetType().Name} ({node}) is not supported")
            };
        }

        private static string BinaryOperatorNameFrom(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.PlusToken => WellKnownMemberNames.AdditionOperatorName,
                SyntaxKind.MinusToken => WellKnownMemberNames.SubtractionOperatorName,
                SyntaxKind.AsteriskToken => WellKnownMemberNames.MultiplyOperatorName,
                SyntaxKind.SlashToken => WellKnownMemberNames.DivisionOperatorName,
                SyntaxKind.PercentToken => WellKnownMemberNames.ModulusOperatorName,
                SyntaxKind.CaretToken => WellKnownMemberNames.ExclusiveOrOperatorName,
                SyntaxKind.AmpersandToken => WellKnownMemberNames.BitwiseAndOperatorName,
                SyntaxKind.BarToken => WellKnownMemberNames.BitwiseOrOperatorName,
                SyntaxKind.EqualsEqualsToken => WellKnownMemberNames.EqualityOperatorName,
                SyntaxKind.LessThanToken => WellKnownMemberNames.LessThanOperatorName,
                SyntaxKind.LessThanEqualsToken => WellKnownMemberNames.LessThanOrEqualOperatorName,
                SyntaxKind.LessThanLessThanToken => WellKnownMemberNames.LeftShiftOperatorName,
                SyntaxKind.GreaterThanToken => WellKnownMemberNames.GreaterThanOperatorName,
                SyntaxKind.GreaterThanEqualsToken => WellKnownMemberNames.GreaterThanOrEqualOperatorName,
                SyntaxKind.GreaterThanGreaterThanToken => WellKnownMemberNames.RightShiftOperatorName,
                SyntaxKind.ExclamationEqualsToken => WellKnownMemberNames.InequalityOperatorName,

                SyntaxKind.ExclamationToken => WellKnownMemberNames.LogicalNotOperatorName,
                SyntaxKind.TildeToken => WellKnownMemberNames.OnesComplementOperatorName,
                _ => throw new Exception($"Cannot map {kind} to a well known member name.")
            };
        }
        private static string UnaryOperatorNameFrom(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.PlusToken => WellKnownMemberNames.UnaryPlusOperatorName,
                SyntaxKind.MinusToken => WellKnownMemberNames.UnaryNegationOperatorName,
                SyntaxKind.BarToken => WellKnownMemberNames.BitwiseOrOperatorName,
                SyntaxKind.ExclamationToken => WellKnownMemberNames.LogicalNotOperatorName,
                SyntaxKind.TildeToken => WellKnownMemberNames.OnesComplementOperatorName,
                SyntaxKind.PlusPlusToken => WellKnownMemberNames.IncrementOperatorName,
                SyntaxKind.MinusMinusToken => WellKnownMemberNames.DecrementOperatorName,
                _ => throw new Exception($"Cannot map {kind} to a well known member name.")
            };
        }
    }
}
