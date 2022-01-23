﻿using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Extensions
{
    internal static class ISymbolExtensions
    {
        public static string FullyQualifiedName(this ISymbol type)
        {
            var format = new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);
            return type.ToDisplayString(format);
        }

        public static bool IsDefinedInCurrentType<T>(this T method, IVisitorContext ctx) where T : ISymbol
        {
            return SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, ctx.SemanticModel.Compilation.Assembly);
        }

        public static bool IsByRef(this ISymbol symbol) =>
            symbol switch
            {
                IParameterSymbol parameterSymbol when parameterSymbol.RefKind != RefKind.None => true,
                IPropertySymbol { ReturnsByRef: true } => true,
                IMethodSymbol { ReturnsByRef: true } => true,
                ILocalSymbol { IsRef: true } => true,
                
                _ => false
            };

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
        
        public static T EnsureNotNull<T>([NotNull][NotNullIfNotNull("symbol")] this T symbol) where T: ISymbol
        {
            if (symbol == null)
                throw new System.NotSupportedException("");

            return symbol;
        }

        public static bool IsDllImportCtor(this ISymbol self) => self != null && self.ContainingType.Name == "DllImportAttribute";
    }
}
