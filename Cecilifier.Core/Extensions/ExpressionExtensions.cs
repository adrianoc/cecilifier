using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.Extensions
{
    public static class ExpressionExtensions
    {
        internal static string EvaluateAsCustomAttributeArgument(this ExpressionSyntax expression, IVisitorContext context)
        {
            return expression.Accept(new CustomAttributeArgumentEvaluator(context));
        }

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
                    context.EmitCilInstruction(ilVar, OpCodes.Ldlen);
                context.EmitCilInstruction(ilVar, OpCodes.Conv_I4);
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
                    context.EmitCilInstruction(ilVar, OpCodes.Box, context.TypeResolver.Resolve(typeInfo.Type));
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

    public class CustomAttributeArgumentEvaluator : CSharpSyntaxVisitor<string>
    {
        private readonly IVisitorContext _context;

        internal CustomAttributeArgumentEvaluator(IVisitorContext context)
        {
            _context = context;
        }

        public override string VisitTypeOfExpression(TypeOfExpressionSyntax node)
        {
            var typeSymbol = _context.SemanticModel.GetTypeInfo(node.Type);
            return _context.TypeResolver.Resolve(typeSymbol.Type);
        }

        public override string? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (node.Expression is IdentifierNameSyntax { Identifier.Text: "nameof" } )
            {
                return $"\"{node.ArgumentList.Arguments[0].Expression}\"";
            }

            return string.Empty;
        }

        public override string VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var type = _context.SemanticModel.GetTypeInfo(node.Expression);
            if (type.Type != null && type.Type.TypeKind == TypeKind.Enum)
            {
                // if this is a enum member reference, return the enum member value.
                var enumMember = (IFieldSymbol) type.Type.GetMembers().Single(member => member.Kind == SymbolKind.Field && member.IsStatic && member.Name == node.Name.Identifier.ValueText);
                return enumMember.ConstantValue.ToString();
            }

            return $"{node.Expression.ToString()}.{node.Name.Identifier.ValueText}";
        }

        public override string VisitLiteralExpression(LiteralExpressionSyntax node) => node.ValueText();

        public override string VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
        {
            if (node.Initializer == null)
            {
                return $"new CustomAttributeArgument[{node.Type.RankSpecifiers[0].Sizes[0]}]";
            }

            var elementType = _context.TypeResolver.Resolve(_context.SemanticModel.GetTypeInfo(node.Type.ElementType).Type);
            return CustomAttributeArgumentArray(node.Initializer, elementType);
        }

        public override string VisitImplicitArrayCreationExpression(ImplicitArrayCreationExpressionSyntax node)
        {
            var elementType = _context.TypeResolver.Resolve(_context.SemanticModel.GetTypeInfo(node.Initializer.Expressions[0]).Type);
            return CustomAttributeArgumentArray(node.Initializer, elementType);
        }

        private string CustomAttributeArgumentArray(InitializerExpressionSyntax initializer, string elementType)
        {
            var sb = new StringBuilder($"new CustomAttributeArgument[] {{");
            foreach (var exp in initializer.Expressions)
            {
                sb.Append($"new CustomAttributeArgument({elementType}, {exp.Accept(this)}),");
            }

            sb.Append("}");
            return sb.ToString();
        }
    }
}
