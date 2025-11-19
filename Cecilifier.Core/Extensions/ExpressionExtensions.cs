using System.Diagnostics;
using System;
using System.Linq;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core.AST;
using Cecilifier.Core.TypeSystem;

namespace Cecilifier.Core.Extensions
{
    public static class ExpressionExtensions
    {
        internal static string ValueText(this LiteralExpressionSyntax node)
        {
            switch (node.Kind())
            {
                case SyntaxKind.StringLiteralExpression:
                    return $"\"{node.Token.ValueText}\"";

                case SyntaxKind.NullLiteralExpression:
                    return "null";

                case SyntaxKind.DefaultLiteralExpression:
                    return "default";

                case SyntaxKind.NumericLiteralExpression:
                case SyntaxKind.TrueLiteralExpression:
                case SyntaxKind.FalseLiteralExpression:
                    return node.Token.ValueText;
            }

            throw new ArgumentException($"{node.Kind()} is not supported.");
        }
        public static void InjectRequiredConversions(this ExpressionSyntax expression, IVisitorContext context, string ilVar, Action loadArrayIntoStack = null)
        {
            var operation = context.SemanticModel.GetOperation(expression);
            if (SymbolEqualityComparer.Default.Equals(operation?.Type, context.RoslynTypeSystem.SystemIndex) && !expression.IsKind(SyntaxKind.IndexExpression) && loadArrayIntoStack != null)
            {
                // We are indexing an array/indexer (this[]) using a System.Index variable; In this case
                // we need to convert from System.Index to *int* which is done through
                // the method System.Index::GetOffset(int32)
                loadArrayIntoStack();
                var indexedType = context.SemanticModel.GetTypeInfo(expression.Ancestors().OfType<ElementAccessExpressionSyntax>().Single().Expression).Type.EnsureNotNull();
                if (indexedType.Name == "Span")
                    context.AddCallToMethod(((IPropertySymbol) indexedType.GetMembers("Length").Single()).GetMethod, ilVar);
                else
                    context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldlen);
                context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Conv_I4);
                context.AddCallToMethod((IMethodSymbol) operation!.Type.GetMembers().Single(m => m.Name == "GetOffset"), ilVar);
            }
            else if (!context.TryApplyConversions(ilVar, operation?.Parent))
            {
                var typeInfo = context.SemanticModel.GetTypeInfo(expression);
                if (typeInfo.Type == null)
                    return;
                
                var conversion = context.SemanticModel.GetConversion(expression);
                if (conversion.IsImplicit && NeedsBoxing(context, expression, typeInfo.Type))
                {
                    context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Box, context.TypeResolver.ResolveAny(typeInfo.Type, ResolveTargetKind.TypeReference));
                }
            }

            // Empirically (verified in generated IL), expressions of type parameter used as:
            //    1. Target of a member reference, unless the type parameter
            //       - is unconstrained (i.e, method being invoked comes from System.Object) or
            //       - is constrained to an interface, but not to a reference type or
            //       - is constrained to 'struct'
            //    2. Source of assignment (or variable initialization) to a reference type
            //    3. Argument for a reference type parameter
            // requires boxing, but for some reason, the conversion returned by GetConversion() does not reflects that. 
            static bool NeedsBoxing(IVisitorContext context, ExpressionSyntax expression, ITypeSymbol type)
            {
                var needsBoxing = type.TypeKind == TypeKind.TypeParameter && 
                                  (NeedsBoxingUsedAsTargetOfReference(context, expression) 
                                   || AssignmentExpressionNeedsBoxing(context, expression, type) 
                                   || TypeIsReferenceType(context, expression, type) 
                                   || expression.Parent.IsArgumentPassedToReferenceTypeParameter(context, type) 
                                   || BinaryExpressionOperandRequiresBoxing(context, expression));
                return needsBoxing;

                bool TypeIsReferenceType(IVisitorContext context, ExpressionSyntax expression, ITypeSymbol rightType)
                {
                    if (expression.Parent is not EqualsValueClauseSyntax) return false;
                    Debug.Assert(expression.Parent.Parent.IsKind(SyntaxKind.VariableDeclarator));

                    // get the type of the declared variable. For instance, in `int x = 10;`, expression='10',
                    // expression.Parent.Parent = 'x=10' (VariableDeclaratorSyntax)
                    var leftType = context.SemanticModel.GetDeclaredSymbol(expression.Parent.Parent).GetMemberType();
                    return !SymbolEqualityComparer.Default.Equals(leftType, rightType) && leftType.IsReferenceType;
                }

                static bool AssignmentExpressionNeedsBoxing(IVisitorContext context, ExpressionSyntax expression, ITypeSymbol rightType)
                {
                    if (expression.Parent is not AssignmentExpressionSyntax assignment) return false;
                    var leftType = context.SemanticModel.GetTypeInfo(assignment.Left).Type;
                    return !SymbolEqualityComparer.Default.Equals(leftType, rightType) && leftType.IsReferenceType;
                }

                static bool NeedsBoxingUsedAsTargetOfReference(IVisitorContext context, ExpressionSyntax expression)
                {
                    if (!expression.Parent.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                        return false;
                    
                    var symbol = context.SemanticModel.GetSymbolInfo(expression).Symbol;
                    // only triggers when expression `T` used in T.Method() (i.e, abstract static methods from an interface)
                    if (symbol is { Kind: SymbolKind.TypeParameter }) return false;
                    ITypeParameterSymbol typeParameter = null;
                    if (symbol == null)
                    {
                        typeParameter = context.GetTypeInfo(expression).Type as ITypeParameterSymbol;
                    }
                    else
                    {
                        // 'expression' represents a local variable, parameter, etc... so we get its element type 
                        typeParameter = symbol.GetMemberType() as ITypeParameterSymbol;
                    }

                    if (typeParameter == null) return false;
                    if (typeParameter.HasValueTypeConstraint) return false;
                    return typeParameter.HasReferenceTypeConstraint || (typeParameter.ConstraintTypes.Length > 0 && typeParameter.ConstraintTypes.Any(candidate => candidate.TypeKind != TypeKind.Interface));
                }
            }
        }

        private static bool BinaryExpressionOperandRequiresBoxing(IVisitorContext context, ExpressionSyntax expression)
        {
            if (expression.Parent is not BinaryExpressionSyntax binaryExpressionSyntax)
                return false;
            
            var left = context.SemanticModel.GetTypeInfo(binaryExpressionSyntax.Left);
            var right = context.SemanticModel.GetTypeInfo(binaryExpressionSyntax.Right);

            var leftType = left.Type ?? left.ConvertedType;
            var rightType = right.Type ?? right.ConvertedType;
            
            return leftType.TypeKind == TypeKind.TypeParameter && rightType.IsReferenceType 
                   || rightType.TypeKind == TypeKind.TypeParameter && leftType.IsReferenceType;
        }
    }
}
