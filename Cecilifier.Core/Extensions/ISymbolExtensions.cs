using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
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

        public static string AssemblyQualifiedName(this ISymbol type)
        {
            var format = new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);
            var namespaceQualifiedName = type.ToDisplayString(format);
            var elementType = type.GetElementType();
            if (elementType == null)
                return namespaceQualifiedName;
            
            return elementType.ContainingAssembly.Name.Contains("CoreLib") ? namespaceQualifiedName : $"{namespaceQualifiedName}, {type.ContainingAssembly.Name}";
        }

        public static ITypeSymbol GetMemberType(this ISymbol symbol) => symbol switch
        {
            IParameterSymbol param => param.Type,
            IMethodSymbol method => method.ReturnType,
            IPropertySymbol property => property.Type,
            IEventSymbol @event => @event.Type,
            IFieldSymbol field => field.Type,
            _ => throw new NotSupportedException($"symbol {symbol.ToDisplayString()} is not supported.")
        };
        
        private static ITypeSymbol GetElementType(this ISymbol symbol) => symbol switch
        {
            IPointerTypeSymbol pointer => pointer.PointedAtType.GetElementType(),
            IFunctionPointerTypeSymbol functionPointer => functionPointer.OriginalDefinition,
            IMethodSymbol method => method.ReturnType,
            IArrayTypeSymbol array => array.ElementType.GetElementType(),
            INamespaceSymbol ns => null,
            _ => (ITypeSymbol) symbol
        };

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

                    acc.Append($",new ParamData {{ IsArray = {isArray}, FullName = \"{elementType.AssemblyQualifiedName()}\", IsTypeParameter = {isTypeParameter} }}");
                    return acc;
                },
                final => final.Remove(0, 1)).Insert(0, "new [] {").Append('}');

            return arrayExp.ToString();
        }

        public static string AsStringNewArrayExpression(this IEnumerable<ITypeSymbol> self)
        {
            return $"new[] {{ {self.Aggregate("", (acc, curr) => acc + ",\"" + curr.AssemblyQualifiedName() + "\"", final => final.Substring(1))} }} ";
        }
        
        public static T EnsureNotNull<T>([NotNull][NotNullIfNotNull("symbol")] this T symbol) where T: ISymbol
        {
            if (symbol == null)
                throw new System.NotSupportedException("");

            return symbol;
        }

        public static bool IsDllImportCtor(this ISymbol self) => self != null && self.ContainingType.Name == "DllImportAttribute";

        public static string AsParameterAttribute(this IParameterSymbol symbol)
        {
            var refRelatedAttr = symbol.RefKind.AsParameterAttribute();
            var optionalAttribute = symbol.HasExplicitDefaultValue ? Constants.ParameterAttributes.Optional : string.Empty;
            if (string.IsNullOrWhiteSpace(refRelatedAttr) && string.IsNullOrWhiteSpace(optionalAttribute))
                return Constants.ParameterAttributes.None;
            
            return refRelatedAttr.AppendModifier(optionalAttribute);
        }

        public static string ExplicitDefaultValue(this IParameterSymbol symbol)
        {
            var value = symbol.ExplicitDefaultValue?.ToString();
            return symbol.Type.SpecialType switch
            {
                SpecialType.System_String => value != null ? $"\"{value}\"" : null,
                SpecialType.System_Single => $"{value}f",
                _ => value
            };
        }
    }
}
