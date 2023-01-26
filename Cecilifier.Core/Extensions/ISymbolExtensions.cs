using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.Extensions
{
    internal static class ISymbolExtensions
    {
        private static readonly SymbolDisplayFormat QualifiedNameWithoutTypeParametersFormat = new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces)
            .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.ExpandNullable)
            .RemoveMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
        
        private static readonly SymbolDisplayFormat QualifiedNameIncludingTypeParametersFormat = QualifiedNameWithoutTypeParametersFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters);

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

        public static string FullyQualifiedName(this ISymbol type, bool includingTypeParameters = true)
        {
            // ISymbol.ToDisplayString() does not have the option to use the metadata name for IntPtr
            // returning `nint` instead.
            if (type is ITypeSymbol { SpecialType: SpecialType.System_IntPtr } ts)
            {
                return "System.IntPtr";
            }

            return type.ToDisplayString(includingTypeParameters ? QualifiedNameIncludingTypeParametersFormat : QualifiedNameWithoutTypeParametersFormat);
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

        public static bool IsDefinedInCurrentAssembly<T>(this T method, IVisitorContext ctx) where T : ISymbol
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
        
        public static string ValueForDefaultLiteral(this ITypeSymbol literalType) => literalType switch
        {
            { SpecialType: SpecialType.System_Char } => "0",
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
        
        public static IMethodSymbol ParameterlessCtor(this ITypeSymbol self) => self?.GetMembers(".ctor").OfType<IMethodSymbol>().Single(ctor => ctor.Parameters.Length == 0);
        public static IMethodSymbol Ctor(this ITypeSymbol self, params ITypeSymbol []parameters) => self?.GetMembers(".ctor")
                                                                                                .OfType<IMethodSymbol>()
                                                                                                .Single(ctor => ctor.Parameters.Select(p => p.Type).SequenceEqual(parameters, SymbolEqualityComparer.Default));
    }
}
