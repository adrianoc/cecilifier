using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using Cecilifier.Core.ApiDriver.Attributes;
using Cecilifier.Core.AST;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Cecilifier.Core.Extensions
{
    public static class SyntaxNodeExtensions
    {
        /// <summary>
        /// Checks whether the given <paramref name="node"/> is the name in a qualified reference.
        /// </summary>
        /// <param name="node">Node with the name of the type/member to be tested.</param>
        /// <returns>true if <paramref name="node"/> is the name in a qualified access, false otherwise</returns>
        /// <remarks>
        /// Examples of qualified / unqualified access:
        /// 1. in, `Foo f = new NS.Foo();`, `Foo` : Foo f => Unqualified, NS.Foo => Qualified
        /// 2. `o.field ?? otherField`, otherField => Unqualified, `field` in `o.field` => Qualified
        /// </remarks>
        public static bool IsMemberAccessThroughImplicitThis(this SyntaxNode node) => node.Parent switch
        {
            MemberAccessExpressionSyntax mae => mae.Name != node && mae.IsMemberAccessThroughImplicitThis(),
            MemberBindingExpressionSyntax mbe => mbe.Name != node, // A MemberBindExpression represents `?.` in the null conditional operator, for instance, `o?.member`
            NameColonSyntax => false, // A NameColon syntax represents the `Length: 42` in an expression like `o as string { Length: 42 }`. In this case, `Length` is equivalent to `o.Length`
            ExpressionColonSyntax => false, // `o as Uri { Host.Length : 10 }`. Parent of `Host.Length` (which is equivalent to `o.Host.Length`) is an ExpressionColonSyntax
            _ => true
        };

        /// <summary>
        /// Returns a human readable summary of the <paramref name="node"/> containing nodes/tokens until (including) first one with a new line trivia.
        /// </summary>
        /// <param name="node"></param>
        /// <returns>human readable summary of the <paramref name="node"/></returns>
        /// <remarks>
        /// Any leading / trailing new lines are removed
        /// </remarks>
        public static string HumanReadableSummary(this SyntaxNode node)
        {
            var nodeAsString = node.ToString();
            var newLineIndex = nodeAsString.IndexOf('\n');
            if (newLineIndex == -1)
                return nodeAsString;

            return nodeAsString.Substring(0, newLineIndex) + "...";
        }

        public static string SourceDetails(this SyntaxNode node) => $"{node} ({node.SyntaxTree.GetMappedLineSpan(node.Span).Span})";

        public static bool IsOperatorOnCustomUserType(this SyntaxNode self, SemanticModel semanticModel, out IMethodSymbol method)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(self);
            method = symbolInfo.Symbol as IMethodSymbol;

            if (method?.ContainingType?.SpecialType == SpecialType.System_Object)
            {
                // special case for object:  == & != are handled by its respective overloaded operators.
                // observe below that `string` behaves exactly the opposite of this.
                return method.Name != "op_Equality" && method.Name != "op_Inequality";
            }
            
            if (method?.ContainingType?.SpecialType == SpecialType.System_String)
            {
                // for strings, == & != are handled by its respective overloaded operators.
                // other operators, like +, are mapped to a specific method call 
                return method.Name == "op_Equality" || method.Name == "op_Inequality";
            }

            // Unmanaged types https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/unmanaged-types operators are handled by the VM 
            // (as opposed to their own operator overloads) whence the check for IsUnmanagedType; the extra check (SpecialType) is meant to filter out
            // custom structs (non primitives) which may be deemed as unmanaged (see the link above for more details)
            return method is { Parameters: { Length: > 0 } }
                   && method.Parameters[0].Type.BaseType?.SpecialType != SpecialType.System_MulticastDelegate
                   && ((!method.Parameters[0].Type.IsUnmanagedType && method.Parameters[0].Type.SpecialType != SpecialType.System_String)
                       || (method.Parameters[0].Type.SpecialType == SpecialType.None && method.Parameters[0].Type.Kind != SymbolKind.PointerType));
        }

        public static IList<ParameterSyntax> ParameterList(this LambdaExpressionSyntax lambdaExpressionSyntax) => lambdaExpressionSyntax switch
        {
            SimpleLambdaExpressionSyntax simple => new[] { simple.Parameter },
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters.ToArray(),
            _ => throw new NotSupportedException($"Lambda type `{lambdaExpressionSyntax.GetType().Name}` is not supported: {lambdaExpressionSyntax}")
        };

        public static IList<TypeParameterSyntax> CollectOuterTypeArguments(this TypeDeclarationSyntax typeArgumentProvider)
        {
            var typeArgs = new List<TypeParameterSyntax>();
            CollectOuterTypeArguments(typeArgumentProvider, typeArgs);
            return typeArgs;
        }

        private static void CollectOuterTypeArguments(TypeDeclarationSyntax typeArgumentProvider, List<TypeParameterSyntax> collectTo)
        {
            if (typeArgumentProvider?.Parent is TypeDeclarationSyntax outer)
            {
                if (outer.TypeParameterList != null)
                    collectTo.AddRange(outer.TypeParameterList.Parameters);
                CollectOuterTypeArguments(outer, collectTo);
            }
        }

        public static MethodDispatchInformation MethodDispatchInformation(this SyntaxNode nodeBeingAccessed)
        {
            if (nodeBeingAccessed is MemberAccessExpressionSyntax mae)
            {
                return mae.Expression.Kind() switch
                {
                    SyntaxKind.ObjectCreationExpression => AST.MethodDispatchInformation.MostLikelyNonVirtual,
                    SyntaxKind.BaseExpression => AST.MethodDispatchInformation.NonVirtual,
                    _ => AST.MethodDispatchInformation.MostLikelyVirtual
                };
            }
            
            return nodeBeingAccessed.IsKind(SyntaxKind.NameColon)  ? AST.MethodDispatchInformation.MostLikelyVirtual : AST.MethodDispatchInformation.MostLikelyNonVirtual ;
        }

        #nullable enable
        [return: NotNull]
        public static TTarget EnsureNotNull<TSource, TTarget>([NotNullIfNotNull("source")] this TSource? source, [CallerArgumentExpression("source")] string? exp = null) where TTarget : TSource
        {
            if (source == null)
                throw new ArgumentNullException(exp);

            return (TTarget) source;
        }
        #nullable restore
        
        internal static bool IsPassedAsInParameter(this ArgumentSyntax toBeChecked, IVisitorContext context)
        {
            var argumentOperation = (IArgumentOperation) context.SemanticModel.GetOperation(toBeChecked);
            return argumentOperation.Parameter.RefKind == RefKind.In;
        }
        
        internal static bool IsArgumentPassedToReferenceTypeParameter(this SyntaxNode toBeChecked, IVisitorContext context, ITypeSymbol typeToNotMatch = null)
        {
            if (!toBeChecked.IsKind(SyntaxKind.Argument))
                return false;
                        
            var argumentOperation = context.SemanticModel.GetOperation(toBeChecked).EnsureNotNull<IOperation, IArgumentOperation>();
            if (argumentOperation.Parameter == null || !argumentOperation.Parameter.Type.IsReferenceType)
                return false;
            
            return typeToNotMatch == null || !SymbolEqualityComparer.Default.Equals(argumentOperation.Parameter.Type, typeToNotMatch);
        }

        internal static bool IsMemberAccessOnElementAccess(this SyntaxNode self) => self.IsKind(SyntaxKind.ElementAccessExpression) && self.Parent is MemberAccessExpressionSyntax mae && mae.Expression == self;

        internal static bool TryGetLiteralValueFor<T>(this ExpressionSyntax expressionSyntax, out T value)
        {
            value = default;
            if (expressionSyntax is not LiteralExpressionSyntax literalExpression)
                return false;

            value = (T) literalExpression.Token.Value;
            return true;
        }

        internal static bool IsUsedAsReturnValueOfType(this SyntaxNode self, SemanticModel semanticModel)
        {
            if (self.Parent == null)
                return false;

            if (self.Parent.IsKind(SyntaxKind.ReturnStatement) || self.Parent.IsKind(SyntaxKind.ArrowExpressionClause))
            {
                var type = semanticModel.GetTypeInfo(self.Parent).Type;
                return true;
            }

            return false;
        }

        internal static IEnumerable<SyntaxToken> ModifiersExcludingAccessibility(this MemberDeclarationSyntax member)
        {
            return member.Modifiers.ExceptBy(
                [SyntaxKind.PublicKeyword, SyntaxKind.PrivateKeyword, SyntaxKind.InternalKeyword, SyntaxKind.ProtectedKeyword],
                c => (SyntaxKind) c.RawKind);
        }

        internal static bool IsStatic(this MemberDeclarationSyntax member) => member.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));

        internal static IEnumerable<CustomAttributeArgument> ToCustomAttributeArguments(this AttributeArgumentListSyntax self, IVisitorContext context)
        {
            if (self == null)
                yield break;
            
            foreach (var argument in self.Arguments)
            {
                var constantValue = GetConstantValue(context, argument.Expression);
                if (argument.NameEquals == null)
                {
                    yield return new CustomAttributeArgument { Value = constantValue.Value };
                    continue;
                }

                var namedArgumentSymbol = context.SemanticModel.GetSymbolInfo(argument.NameEquals!.Name).Symbol;
                yield return new CustomAttributeNamedArgument
                {
                    Name= argument.NameEquals!.Name.Identifier.Text, 
                    Value = constantValue.Value, 
                    Kind = namedArgumentSymbol!.Kind == SymbolKind.Field ? NamedArgumentKind.Field : NamedArgumentKind.Property,
                    ResolvedType = context.TypeResolver.ResolveAny(namedArgumentSymbol.GetMemberType(), ResolveTargetKind.Parameter)
                };
            }
        }

        private readonly record struct RawCSharpCode(string Code)
        {
            public override string ToString() => Code;
        }
        
        private static Optional<object> GetConstantValue(IVisitorContext context, ExpressionSyntax expression)
        {
            return expression switch
            {
                TypeOfExpressionSyntax typeOf => new RawCSharpCode(context.TypeResolver.ResolveAny(context.SemanticModel.GetTypeInfo(typeOf.Type).Type, ResolveTargetKind.TypeReference).Expression),
                ImplicitArrayCreationExpressionSyntax implicitArrayCreation => ConstantValueForArray(implicitArrayCreation.Initializer),
                ArrayCreationExpressionSyntax arrayCreation => ConstantValueForArray(arrayCreation.Initializer),
                
                _ => context.SemanticModel.GetConstantValue(expression)
            };

            Optional<object> ConstantValueForArray(InitializerExpressionSyntax initializer)
            {
                if (initializer == null)
                {
                    return Array.Empty<object>();
                }

                var array = new object[initializer.Expressions.Count];
                int i = 0;
                foreach (var exp in initializer.Expressions)
                {
                    array[i++] = context.SemanticModel.GetConstantValue(exp).Value;
                }

                return array;
            }
        }
    }
}
