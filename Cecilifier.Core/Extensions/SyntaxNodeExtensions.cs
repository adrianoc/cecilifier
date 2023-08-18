using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Cecilifier.Core.Extensions
{
    public static class SyntaxNodeExtensions
    {
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
                   && method.Parameters[0].Type?.BaseType?.SpecialType != SpecialType.System_MulticastDelegate
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

        public static bool IsAccessOnThisOrObjectCreation(this SyntaxNode nodeBeingAccessed)
        {
            if (nodeBeingAccessed.IsKind(SyntaxKind.NameColon))
                return false;
            
            if (nodeBeingAccessed is MemberAccessExpressionSyntax mae)
            {
                return mae.Expression.IsKind(SyntaxKind.ObjectCreationExpression);
            }

            return true;
        }

        [return: NotNull]
        public static TTarget EnsureNotNull<TSource, TTarget>([NotNullIfNotNull("source")] this TSource source, [CallerArgumentExpression("source")] string exp = null) where TTarget : TSource
        {
            if (source == null)
                throw new ArgumentNullException(exp);

            return (TTarget) source;
        }
        
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
    }
}
