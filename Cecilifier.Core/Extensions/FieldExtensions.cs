using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Extensions
{
    internal static class FieldExtensions
    {
        public static string FieldResolverExpression(this IFieldSymbol field, IVisitorContext context) => context.MemberResolver.ResolveField(field);
    }
}
