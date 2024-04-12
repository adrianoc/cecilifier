using System;
using System.Diagnostics;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST;

partial class ExpressionVisitor
{
    private void InjectRequiredConversions(ExpressionSyntax expression, Action loadArrayIntoStack = null)
    {
        var typeInfo = ModelExtensions.GetTypeInfo(Context.SemanticModel, expression);
        if (typeInfo.Type == null) return;
        var conversion = Context.SemanticModel.GetConversion(expression);
        if (conversion.IsImplicit)
        {
            if (conversion.IsNullable)
            {
                Context.EmitCilInstruction(
                    ilVar, 
                    OpCodes.Newobj,
                    $"assembly.MainModule.ImportReference(typeof(System.Nullable<>).MakeGenericType(typeof({typeInfo.Type.FullyQualifiedName()})).GetConstructors().Single(ctor => ctor.GetParameters().Length == 1))");
                return;
            }

            if (conversion.IsNumeric)
            {
                Debug.Assert(typeInfo.ConvertedType != null);
                switch (typeInfo.ConvertedType.SpecialType)
                {
                    case SpecialType.System_Single:
                        Context.EmitCilInstruction(ilVar, OpCodes.Conv_R4);
                        return;
                    case SpecialType.System_Double:
                        Context.EmitCilInstruction(ilVar, OpCodes.Conv_R8);
                        return;
                    case SpecialType.System_Byte:
                        Context.EmitCilInstruction(ilVar, OpCodes.Conv_I1);
                        return;
                    case SpecialType.System_Int16:
                        Context.EmitCilInstruction(ilVar, OpCodes.Conv_I2);
                        return;
                    case SpecialType.System_Int32:
                        // byte/char are pushed as Int32 by the runtime 
                        if (typeInfo.Type.SpecialType != SpecialType.System_SByte && typeInfo.Type.SpecialType != SpecialType.System_Byte && typeInfo.Type.SpecialType != SpecialType.System_Char)
                            Context.EmitCilInstruction(ilVar, OpCodes.Conv_I4);
                        return;
                    case SpecialType.System_Int64:
                        var convOpCode = typeInfo.Type.SpecialType == SpecialType.System_Char || typeInfo.Type.SpecialType == SpecialType.System_Byte ? OpCodes.Conv_U8 : OpCodes.Conv_I8;
                        Context.EmitCilInstruction(ilVar, convOpCode);
                        return;
                    case SpecialType.System_Decimal:
                        var operand = typeInfo.ConvertedType.GetMembers().OfType<IMethodSymbol>()
                            .Single(m => m.MethodKind == MethodKind.Constructor && m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == typeInfo.Type.SpecialType);
                        Context.EmitCilInstruction(ilVar, OpCodes.Newobj, operand.MethodResolverExpression(Context));
                        return;
                    default:
                        throw new Exception($"Conversion from {typeInfo.Type} to {typeInfo.ConvertedType}  not implemented.");
                }
            }

            if (conversion.MethodSymbol != null)
            {
                AddMethodCall(ilVar, conversion.MethodSymbol, MethodCallOptions.None);
            }
        }

        if (conversion.IsImplicit && (conversion.IsBoxing || NeedsBoxing(Context, expression, typeInfo.Type)))
        {
            AddCilInstruction(ilVar, OpCodes.Box, typeInfo.Type);
        }
        else if (conversion.IsIdentity && typeInfo.Type.Name == "Index" && !expression.IsKind(SyntaxKind.IndexExpression) && loadArrayIntoStack != null)
        {
            // We are indexing an array/indexer (this[]) using a System.Index variable; In this case
            // we need to convert from System.Index to *int* which is done through
            // the method System.Index::GetOffset(int32)
            loadArrayIntoStack();
            var indexed = ModelExtensions.GetTypeInfo(Context.SemanticModel, expression.Ancestors().OfType<ElementAccessExpressionSyntax>().Single().Expression);
            Utils.EnsureNotNull(indexed.Type, "Cannot be null.");
            if (indexed.Type.Name == "Span")
                AddMethodCall(ilVar, ((IPropertySymbol) indexed.Type.GetMembers("Length").Single()).GetMethod, MethodCallOptions.None);
            else
                Context.EmitCilInstruction(ilVar, OpCodes.Ldlen);
            Context.EmitCilInstruction(ilVar, OpCodes.Conv_I4);
            AddMethodCall(ilVar, (IMethodSymbol) typeInfo.Type.GetMembers().Single(m => m.Name == "GetOffset"), MethodCallOptions.None);
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
                var leftType = ModelExtensions.GetTypeInfo(context.SemanticModel, assignment.Left).Type;
                return !SymbolEqualityComparer.Default.Equals(leftType, rightType) && leftType.IsReferenceType;
            }

            static bool NeedsBoxingUsedAsTargetOfReference(IVisitorContext context, ExpressionSyntax expression)
            {
                if (!expression.Parent.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                    return false;
                
                var symbol = ModelExtensions.GetSymbolInfo(context.SemanticModel, expression).Symbol;
                // only triggers when expression `T` used in T.Method() (i.e, abstract static methods from an interface)
                if (symbol is { Kind: SymbolKind.TypeParameter }) return false;
                ITypeParameterSymbol typeParameter = null;
                if (symbol == null)
                {
                    typeParameter = context.GetTypeInfo(expression).Type as ITypeParameterSymbol;
                }
                else
                {
                    // 'expression' represents a local variable, parameters, etc.. so we get its element type 
                    typeParameter = symbol.GetMemberType() as ITypeParameterSymbol;
                }

                if (typeParameter == null) return false;
                if (typeParameter.HasValueTypeConstraint) return false;
                return typeParameter.HasReferenceTypeConstraint || (typeParameter.ConstraintTypes.Length > 0 && typeParameter.ConstraintTypes.Any(candidate => candidate.TypeKind != TypeKind.Interface));
            }
        }
    }
}
