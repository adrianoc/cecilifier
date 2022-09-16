using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.Extensions
{
    internal static class ISymbolExtensions
    {
        private static readonly SymbolDisplayFormat FullyQualifiedDisplayFormat = new(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

        public static string FullyQualifiedName(this ISymbol type)
        {
            return type.ToDisplayString(FullyQualifiedDisplayFormat);
        }
        
        public static string SafeIdentifier(this IMethodSymbol method)
        {
            return method.MethodKind == MethodKind.Constructor 
                ? "ctor" 
                : method.Name;
        }

        public static string AssemblyQualifiedName(this ISymbol type)
        {
            var format = new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);
            var namespaceQualifiedName = type.ToDisplayString(format);
            var elementType = type.GetElementType();
            if (elementType == null)
                return namespaceQualifiedName;
            
            return elementType.ContainingAssembly.Name.Contains("CoreLib") ? namespaceQualifiedName : $"{namespaceQualifiedName}, {type.GetElementType().ContainingAssembly.Name}";
        }

        public static ITypeSymbol GetMemberType(this ISymbol symbol) => symbol switch
        {
            IParameterSymbol param => param.Type,
            IMethodSymbol method => method.ReturnType,
            IPropertySymbol property => property.Type,
            IEventSymbol @event => @event.Type,
            IFieldSymbol field => field.Type,
            ILocalSymbol local => local.Type,
            _ => throw new NotSupportedException($"({symbol.Kind}) symbol {symbol.ToDisplayString()} is not supported.")
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
        
        [return:NotNull] public static T EnsureNotNull<T>([NotNullIfNotNull("symbol")] this T symbol) where T: ISymbol
        {
            if (symbol == null)
                throw new System.NotSupportedException("");

            return symbol;
        }
        
        [return:NotNull] public static TTarget EnsureNotNull<TSource, TTarget>([NotNull][NotNullIfNotNull("symbol")] this TSource symbol, [CallerArgumentExpression("symbol")] string exp = null) where TSource: ISymbol where TTarget : TSource
        {
            if (symbol == null)
                throw new NullReferenceException(exp);

            return (TTarget) symbol;
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
        
        public static DefinitionVariable EnsureFieldExists(this IFieldSymbol fieldSymbol, [NotNull] IVisitorContext context, [NotNull] SimpleNameSyntax node)
        {
            var declaringSyntaxReference = fieldSymbol.DeclaringSyntaxReferences.SingleOrDefault();
            if (declaringSyntaxReference == null)
                return DefinitionVariable.NotFound;
            
            var fieldDeclaration = (FieldDeclarationSyntax) declaringSyntaxReference.GetSyntax().Parent.Parent;
            if (fieldDeclaration.Span.Start > node.Span.End)
            {
                // this is a forward reference, process it...
                fieldDeclaration.Accept(new FieldDeclarationVisitor(context));
            }
            
            var fieldDeclarationVariable = context.DefinitionVariables.GetVariable(fieldSymbol.Name, VariableMemberKind.Field, fieldSymbol.ContainingType.ToDisplayString());
            if (!fieldDeclarationVariable.IsValid)
                throw new Exception($"Could not resolve reference to field: {fieldSymbol.Name}");
            
            return fieldDeclarationVariable;
        }
        
        public static void EnsurePropertyExists(this IPropertySymbol propertySymbol, IVisitorContext context, [NotNull] SyntaxNode node)
        {
            var declaringReference = propertySymbol.DeclaringSyntaxReferences.SingleOrDefault();
            if (declaringReference == null)
                return;
            
            var propertyDeclaration = (BasePropertyDeclarationSyntax) declaringReference.GetSyntax();
            if (propertyDeclaration.Span.Start > node.Span.End)
            {
                // this is a forward reference, process it...
                propertyDeclaration.Accept(new PropertyDeclarationVisitor(context));
            }
        }
       
        public static OpCode LoadOpCodeForFieldAccess(this ISymbol symbol) => symbol.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld;
        public static OpCode StoreOpCodeForFieldAccess(this ISymbol symbol) => symbol.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld;
    }
}
