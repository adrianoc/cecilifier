using System;
using System.Diagnostics;
using System.Linq;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST;

partial class ExpressionVisitor
{
    private void InjectRequiredConversions(ExpressionSyntax expression, Action loadArrayIntoStack = null)
    {
        var operation = Context.SemanticModel.GetOperation(expression);
        if (SymbolEqualityComparer.Default.Equals(operation?.Type, Context.RoslynTypeSystem.SystemIndex) && !expression.IsKind(SyntaxKind.IndexExpression) && loadArrayIntoStack != null)
        {
            // We are indexing an array/indexer (this[]) using a System.Index variable; In this case
            // we need to convert from System.Index to *int* which is done through
            // the method System.Index::GetOffset(int32)
            loadArrayIntoStack();
            var indexedType = Context.SemanticModel.GetTypeInfo(expression.Ancestors().OfType<ElementAccessExpressionSyntax>().Single().Expression).Type.EnsureNotNull();
            if (indexedType.Name == "Span")
                Context.AddCallToMethod(((IPropertySymbol) indexedType.GetMembers("Length").Single()).GetMethod, ilVar);
            else
                Context.EmitCilInstruction(ilVar, OpCodes.Ldlen);
            Context.EmitCilInstruction(ilVar, OpCodes.Conv_I4);
            Context.AddCallToMethod((IMethodSymbol) operation!.Type.GetMembers().Single(m => m.Name == "GetOffset"), ilVar);
        }
        else if (!Context.TryApplyConversions(ilVar, operation?.Parent))
        {
            var typeInfo = Context.SemanticModel.GetTypeInfo(expression);
            if (typeInfo.Type == null)
                return;
            
            var conversion = Context.SemanticModel.GetConversion(expression);
            if (conversion.IsImplicit && NeedsBoxing(Context, expression, typeInfo.Type))
            {
                AddCilInstruction(ilVar, OpCodes.Box, typeInfo.Type);
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
            var needsBoxing = type.TypeKind == TypeKind.TypeParameter && (NeedsBoxingUsedAsTargetOfReference(context, expression) || AssignmentExpressionNeedsBoxing(context, expression, type) ||
                                                                          TypeIsReferenceType(context, expression, type) || expression.Parent.IsArgumentPassedToReferenceTypeParameter(context, type) ||
                                                                          expression.Parent is BinaryExpressionSyntax binaryExpressionSyntax && binaryExpressionSyntax.OperatorToken.IsKind(SyntaxKind.IsKeyword));
            return needsBoxing;

            bool TypeIsReferenceType(IVisitorContext context, ExpressionSyntax expression, ITypeSymbol rightType)
            {
                if (expression.Parent is not EqualsValueClauseSyntax equalsValueClauseSyntax) return false;
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
}
