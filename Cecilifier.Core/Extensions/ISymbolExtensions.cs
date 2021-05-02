using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Extensions
{
    internal static class ISymbolExtensions
    {
        public static bool IsDefinedInCurrentType<T>(this T method, IVisitorContext ctx) where T : ISymbol
        {
            return method.ContainingAssembly == ctx.SemanticModel.Compilation.Assembly;
        }

        public static bool IsByRef(this ISymbol symbol)
        {
            if (symbol is IParameterSymbol parameterSymbol && parameterSymbol.RefKind != RefKind.None)
                return true;

            if (symbol is ILocalSymbol { IsRef: true})
                return true;
            
            return false;
        }

        public static string AsStringNewArrayExpression(this IEnumerable<IParameterSymbol> self)
        {
            if (!self.Any())
            {
                return "new ParamData[0]";
            }

            var sb = new StringBuilder();
            var arrayExp = self.Aggregate(sb,
                (acc, curr) =>
                {
                    var elementType = (curr.Type as IArrayTypeSymbol)?.ElementType ?? curr.Type;
                    var isArray = curr.Type is IArrayTypeSymbol ? "true" : "false";
                    var isTypeParameter = elementType.Kind == SymbolKind.TypeParameter ? "true" : "false";

                    acc.Append($",new ParamData {{ IsArray = {isArray}, FullName = \"{elementType.FullyQualifiedName()}\", IsTypeParameter = {isTypeParameter} }}");
                    return acc;
                },
                final => final.Remove(0, 1)).Insert(0, "new [] {").Append('}');

            return arrayExp.ToString();
        }

        public static string AsStringNewArrayExpression(this IEnumerable<ITypeSymbol> self)
        {
            return $"new[] {{ {self.Aggregate("", (acc, curr) => acc + ",\"" + curr.FullyQualifiedName() + "\"", final => final.Substring(1))} }} ";
        }
    }
}
