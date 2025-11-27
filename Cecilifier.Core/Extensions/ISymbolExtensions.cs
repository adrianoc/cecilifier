#nullable enable annotations

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core.AST;
using Cecilifier.Core.AST.Params;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;

namespace Cecilifier.Core.Extensions
{
    public static class ISymbolExtensions
    {
        private static readonly SymbolDisplayFormat QualifiedNameWithoutTypeParametersFormat = new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces)
            .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.ExpandNullable)
            .RemoveMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        private static readonly SymbolDisplayFormat QualifiedNameIncludingTypeParametersFormat = QualifiedNameWithoutTypeParametersFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters);
        private static readonly SymbolDisplayFormat ValidVariableNameFormat = new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly)
                .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.ExpandNullable)
                .RemoveMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        public static ElementKind ToElementKind(this TypeKind self) => self switch
        {
            TypeKind.Class => ElementKind.Class,
            TypeKind.Enum => ElementKind.Enum,
            TypeKind.Struct => ElementKind.Struct,
            TypeKind.Interface => ElementKind.Interface,
            TypeKind.Delegate => ElementKind.Delegate,
            TypeKind.Array => ElementKind.None,
            TypeKind.TypeParameter => ElementKind.None,

            _ => throw new NotImplementedException($"TypeKind `{self}` is not supported.")
        };

        public static string SafeIdentifier(this IMethodSymbol method)
        {
            return method.MethodKind == MethodKind.Constructor
                ? "ctor"
                : method.Name;
        }

        /// <summary>
        /// Returns a mangled name matching C# compiler name rules as of Oct/2025 if <paramref name="method"/> represents a local function otherwise <paramref name="method"/>.Name.
        /// </summary>
        /// <param name="method"></param>
        /// method to return name for. 
        /// <returns>a name appropriate for the passed method.</returns>
        public static string MappedName(this IMethodSymbol method) => method.MethodKind == MethodKind.LocalFunction 
                                                                            ? $"<{method.ContainingSymbol.Name}>g__{method.Name}|0_0"
                                                                            : method.Name;

        public static string FullyQualifiedName(this ISymbol symbol, bool includingTypeParameters = true)
        {
            return symbol.ToDisplayString(includingTypeParameters ? QualifiedNameIncludingTypeParametersFormat : QualifiedNameWithoutTypeParametersFormat);
        }
        
        public static string ToValidVariableName(this ISymbol symbol)
        {
            return symbol switch
            {
                IMethodSymbol { MethodKind: MethodKind.Conversion} method => method.ReturnType.ToDisplayString(ValidVariableNameFormat),
                IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator} method => method.Name,
                IMethodSymbol method => method.ToDisplayString(ValidVariableNameFormat),
                _ => symbol.ToDisplayString(ValidVariableNameFormat)
            };
        }
        
        public static string GetReflectionName(this ITypeSymbol typeSymbol)
        {
            var sb = new System.Text.StringBuilder();

            if (typeSymbol is IArrayTypeSymbol array)
            {
                return $"{GetReflectionName(array.ElementType)}[{new String(',', array.Rank - 1)}]";
            }
            
            // Append the namespace if it's not a global namespace
            if (!typeSymbol.ContainingNamespace.IsGlobalNamespace)
            {
                sb.Append($"{typeSymbol.ContainingNamespace.ToDisplayString()}.");
            }
    
            sb.Append(typeSymbol.MetadataName);
    
            if (typeSymbol is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: > 0 } namedTypeSymbol)
            {
                sb.Append($"[[{string.Join(", ", namedTypeSymbol.TypeArguments.Select(t => t.GetReflectionName()))}]]");
            }
    
            if (typeSymbol.ContainingType != null)
            {
                sb.Insert(0, $"{typeSymbol.ContainingType.GetReflectionName()}+");
            }

            return sb.ToString();
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

        public static bool IsDefinedInCurrentAssembly<T>(this T symbol, IVisitorContext ctx) where T : ISymbol
        {
            return SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, ctx.SemanticModel.Compilation.Assembly);
        }

        public static bool IsByRef(this ISymbol symbol) =>
            symbol switch
            {
                IParameterSymbol { RefKind: RefKind.In or RefKind.Out or RefKind.RefReadOnlyParameter or RefKind.Ref } => true,
                IPropertySymbol { ReturnsByRef: true } => true,
                IPropertySymbol { ReturnsByRefReadonly: true } => true,
                IMethodSymbol { ReturnsByRef: true } => true,
                IMethodSymbol { ReturnsByRefReadonly: true } => true,
                ILocalSymbol { IsRef: true } => true,
                IFieldSymbol { RefKind: not RefKind.None } => true,

                _ => false
            };

        [ExcludeFromCodeCoverage]
        [return: NotNull]
        public static T EnsureNotNull<T>([NotNullIfNotNull("symbol")] this T? symbol, [CallerArgumentExpression(nameof(symbol))] string expression = null) where T : ISymbol
        {
            if (symbol == null)
                throw new NullReferenceException($"Expression '{expression}' is expected to be non null.");

            return symbol;
        }

        public static string AsParameterAttribute(this IParameterSymbol symbol)
        {
            var refRelatedAttr = symbol.RefKind.AsParameterAttribute();
            var optionalAttribute = symbol.HasExplicitDefaultValue ? Constants.ParameterAttributes.Optional : string.Empty;
            if (string.IsNullOrWhiteSpace(refRelatedAttr) && string.IsNullOrWhiteSpace(optionalAttribute))
                return Constants.ParameterAttributes.None;

            return refRelatedAttr.AppendModifier(optionalAttribute);
        }

        public static (string Value, bool Present) ExplicitDefaultValue(this IParameterSymbol symbol, bool rawString = true)
        {
            if (!symbol.HasExplicitDefaultValue)
                return (null, false);

            if (symbol.ExplicitDefaultValue == null)
                return (null, true);

            if (symbol.Type.SpecialType == SpecialType.System_String && rawString)
                return ((string)symbol.ExplicitDefaultValue, true);
            
            var value = SymbolDisplay.FormatPrimitive(symbol.ExplicitDefaultValue, !rawString, false);
            return symbol.Type.SpecialType switch
            {
                SpecialType.System_Single => ($"{value}f", true),
                SpecialType.System_Double => ($"{value}d", true),
                _ => (value, true)
            };
        }

        public static void EnsureFieldExists(this IFieldSymbol fieldSymbol, IVisitorContext context, SimpleNameSyntax node)
        {
            if (fieldSymbol.ContainingType?.TypeKind == TypeKind.Enum)
                return; // Enum members can never be forward referenced.
            
            var declaringSyntaxReference = fieldSymbol.DeclaringSyntaxReferences.SingleOrDefault();
            if (declaringSyntaxReference == null)
                return;
            
            var fieldDeclaration = declaringSyntaxReference.GetSyntax().Parent.Parent.EnsureNotNull<SyntaxNode,FieldDeclarationSyntax>();
            if (fieldDeclaration.Span.Start > node.Span.End)
            {
                // this is a forward reference, process it...
                fieldDeclaration.Accept(new FieldDeclarationVisitor(context));
            }

            var fieldDeclarationVariable = context.DefinitionVariables.GetVariable(fieldSymbol.Name, VariableMemberKind.Field, fieldSymbol.ContainingType.OriginalDefinition.ToDisplayString());
            fieldDeclarationVariable.ThrowIfVariableIsNotValid();
        }

        public static void EnsurePropertyExists(this IPropertySymbol propertySymbol, IVisitorContext context, [NotNull] SyntaxNode node)
        {
            var declaringReference = propertySymbol.DeclaringSyntaxReferences.SingleOrDefault();
            if (declaringReference == null)
                return;

            var propertyDeclaration = (CSharpSyntaxNode) declaringReference.GetSyntax();
            if (propertyDeclaration.Span.Start > node.Span.End)
            {
                // this is a forward reference, process it...
                propertyDeclaration.Accept(new PropertyDeclarationVisitor(context));
            }
        }

        public static bool HasCovariantGetter(this IPropertySymbol property) => property.IsOverride && !SymbolEqualityComparer.Default.Equals(property?.OverriddenProperty?.Type, property.Type);

        public static OpCode LoadOpCodeForFieldAccess(this ISymbol symbol) => symbol.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld;
        public static OpCode StoreOpCodeForFieldAccess(this ISymbol symbol) => symbol.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld;

        public static OpCode LoadAddressOpcodeForMember(this ISymbol symbol) => symbol.Kind switch
        {
            SymbolKind.Field => symbol.IsStatic ? OpCodes.Ldsflda : OpCodes.Ldflda,
            SymbolKind.Parameter => OpCodes.Ldarg_S,
            SymbolKind.Local => OpCodes.Ldloca_S,
            _ => throw new ArgumentException($"Invalid symbol type for {symbol} ({symbol.Kind})")
        };

        public static OpCode LoadOpCodeFor(this ITypeSymbol type)
        {
            return type.SpecialType switch
            {
                SpecialType.System_IntPtr => OpCodes.Ldc_I4,
                SpecialType.System_UIntPtr => OpCodes.Ldc_I4,
                SpecialType.System_Single => OpCodes.Ldc_R4,
                SpecialType.System_Double => OpCodes.Ldc_R8,
                SpecialType.System_Byte => OpCodes.Ldc_I4,
                SpecialType.System_SByte => OpCodes.Ldc_I4,
                SpecialType.System_Int16 => OpCodes.Ldc_I4,
                SpecialType.System_Int32 => OpCodes.Ldc_I4,
                SpecialType.System_UInt16 => OpCodes.Ldc_I4,
                SpecialType.System_UInt32 => OpCodes.Ldc_I4,
                SpecialType.System_UInt64 => OpCodes.Ldc_I8,
                SpecialType.System_Int64 => OpCodes.Ldc_I8,
                SpecialType.System_Char => OpCodes.Ldc_I4,
                SpecialType.System_Boolean => OpCodes.Ldc_I4,
                SpecialType.System_String => OpCodes.Ldstr,
                SpecialType.None => type.TypeKind == TypeKind.Enum ? OpCodes.Ldc_I4 : OpCodes.Ldnull,
                
                _ => throw new ArgumentException($"Literal type {type} not supported.", nameof(type))
            };
        }

        public static string? ValueForDefaultLiteral(this ITypeSymbol literalType) => literalType switch
        {
            { SpecialType: SpecialType.System_Char } => "\0",
            { SpecialType: SpecialType.System_SByte } => "0",
            { SpecialType: SpecialType.System_Byte } => "0",
            { SpecialType: SpecialType.System_Int16 } => "0",
            { SpecialType: SpecialType.System_UInt16 } => "0",
            { SpecialType: SpecialType.System_Int32 } => "0",
            { SpecialType: SpecialType.System_UInt32 } => "0",
            { SpecialType: SpecialType.System_Int64 } => "0L",
            { SpecialType: SpecialType.System_UInt64 } => "0L",
            { SpecialType: SpecialType.System_Single } => "0.0F",
            { SpecialType: SpecialType.System_Double } => "0.0D",
            { SpecialType: SpecialType.System_Boolean } => "false",
            { SpecialType: SpecialType.System_IntPtr } => "0",
            { SpecialType: SpecialType.System_UIntPtr } => "0",
            { SpecialType: SpecialType.System_String } => null,
            { TypeKind: TypeKind.TypeParameter } => null,
            { TypeKind: TypeKind.Class } => null,
            { TypeKind: TypeKind.Interface } => null,
            { TypeKind: TypeKind.Struct } => null,
            { TypeKind: TypeKind.Pointer } => null,
            { TypeKind: TypeKind.Delegate } => null,
            _ => throw new ArgumentOutOfRangeException(nameof(literalType), literalType, null)
        };

        public static IMethodSymbol ParameterlessCtor(this ITypeSymbol self) => self.GetMembers(".ctor").OfType<IMethodSymbol>().Single(ctor => ctor.Parameters.Length == 0);
        public static IMethodSymbol Ctor(this ITypeSymbol self, params ITypeSymbol[] parameters) => self.GetMembers(".ctor")
                                                                                                .OfType<IMethodSymbol>()
                                                                                                .Single(ctor => ctor.Parameters.Select(p => p.Type).SequenceEqual(parameters, SymbolEqualityComparer.Default));

        public static VariableMemberKind ToVariableMemberKind(this ISymbol self) => self.Kind switch
        {
            SymbolKind.Field => VariableMemberKind.Field,
            SymbolKind.Local => VariableMemberKind.LocalVariable,
            SymbolKind.Parameter => VariableMemberKind.Parameter,
            _ => throw new ArgumentException($"Invalid symbol type for '{self}' ({self.Kind})")
        };

        public static bool TryGetAttribute<T>(this ISymbol symbol, [NotNullWhen(true)] out AttributeData? attributeData) where T : Attribute
        {
            var typeOfT = typeof(T);
            attributeData = symbol.GetAttributes().SingleOrDefault(attr => attr.AttributeClass?.Name == typeOfT.Name);
            return attributeData != null;
        }
        public static bool HasTypeArgumentOfTypeFromCecilifiedCodeTransitive(this INamedTypeSymbol type, IVisitorContext context)
        {
            return type.TypeArguments.Any(t => t.IsDefinedInCurrentAssembly(context)) 
                   || (type.ContainingType != null && (SymbolEqualityComparer.Default.Equals(type.ContainingType, type) ? false : HasTypeArgumentOfTypeFromCecilifiedCodeTransitive(type.ContainingType, context)));
        }
        
        internal static ExpandedParamsArgumentHandler? CreateExpandedParamsUsageHandler(this IMethodSymbol methodSymbol, ExpressionVisitor expressionVisitor, string ilVar, ArgumentListSyntax argumentList)
        {
            var paramsParameter = methodSymbol.Parameters.FirstOrDefault(p => p.IsParams);
            if (paramsParameter == null || !IsExpandedForm(argumentList, paramsParameter))
            {
                // There's no `params` parameter in the method signature or the argument is being passed in its non-expanded form, i.e. `new type[] {....}` 
                return null;
            }

            var context = expressionVisitor.Context;
            if (IsUnsupportedParamsParameterType(context, paramsParameter, out var extraHelp))
            {
                context.EmitError($"Cecilifier does not support type {paramsParameter.Type} as a 'params' parameter ({paramsParameter.Name}).{extraHelp}", paramsParameter.DeclaringSyntaxReferences.First().GetSyntax());
                return null;
            }
            
            return paramsParameter.Type switch
            {
                IArrayTypeSymbol => new ArrayExpandedParamsArgumentHandler(context, paramsParameter, argumentList, ilVar),
                INamedTypeSymbol namedType when SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, context.RoslynTypeSystem.SystemSpan)  => new SpanExpandedParamsArgumentHandler(context, paramsParameter, argumentList, ilVar),
                INamedTypeSymbol namedType when SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, context.RoslynTypeSystem.SystemReadOnlySpan.Value) => new ReadOnlySpanExpandedParamsArgumentHandler(context, paramsParameter, argumentList, ilVar),
                INamedTypeSymbol namedType when SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, context.RoslynTypeSystem.SystemCollectionsGenericIEnumerableOfT)  => new ReadOnlySpanExpandedParamsArgumentHandler(context, paramsParameter, argumentList, ilVar),
                INamedTypeSymbol namedType when SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, context.RoslynTypeSystem.SystemCollectionsGenericIListOfT)  => new ListBackedExpandedParamsArgumentHandler(expressionVisitor, paramsParameter, argumentList),
                INamedTypeSymbol namedType when SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, context.RoslynTypeSystem.SystemCollectionsGenericICollectionOfT)  => new ListBackedExpandedParamsArgumentHandler(expressionVisitor, paramsParameter, argumentList),
                _ => throw new NotImplementedException($"Type {paramsParameter.Type} is not supported.")
            };
            
            static bool IsExpandedForm(ArgumentListSyntax argumentList, IParameterSymbol paramsParameter)
            {
                var firstParamsArgument = argumentList.Arguments[paramsParameter.Ordinal];
                return !firstParamsArgument.Expression.IsKind(SyntaxKind.ArrayInitializerExpression) && 
                       !firstParamsArgument.Expression.IsKind(SyntaxKind.ImplicitArrayCreationExpression);
            }

            static bool IsUnsupportedParamsParameterType(IVisitorContext context, IParameterSymbol paramsParameter, out string extraHelp)
            {
                if (SymbolEqualityComparer.Default.Equals(paramsParameter.Type.OriginalDefinition, context.RoslynTypeSystem.SystemCollectionsGenericIEnumerableOfT))
                {
                    extraHelp = $" You may change {paramsParameter.Name}' to 'ICollection<T>'";
                    return true;
                }

                if (SymbolEqualityComparer.Default.Equals(paramsParameter.Type.ElementTypeSymbolOf().OriginalDefinition, context.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Nullable_T)))
                {
                    extraHelp = " Nullable<T> (i.e, int?) are not supported yet.";
                    return true;
                }

                extraHelp = string.Empty;
                return false;
            }
        }

        internal static string? ParamsAttributeMatchingType(this IParameterSymbol paramsParameter) => paramsParameter.IsParams 
                                                                                                        ? paramsParameter.Type.Kind ==  SymbolKind.ArrayType ? typeof(ParamArrayAttribute).FullName : typeof(ParamCollectionAttribute).FullName
                                                                                                        : null;
    }
}
