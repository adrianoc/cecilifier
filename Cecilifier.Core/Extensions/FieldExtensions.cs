using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;
using static Cecilifier.Core.Misc.Utils;

namespace Cecilifier.Core.Extensions
{
    internal static class FieldExtensions
    {
        public static string FieldResolverExpression(this IFieldSymbol field, IVisitorContext context)
        {
            if (field.IsDefinedInCurrentType(context))
            {
                return "fld_" + field.ContainingType.Name.CamelCase() + "_" + field.Name.CamelCase();
            }

            var declaringTypeName = field.ContainingType.FullyQualifiedName();

            return ImportFromMainModule($"TypeHelpers.ResolveField(\"{declaringTypeName}\",\"{field.Name}\")");
        }
    }
}
