using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Cecilifier.Core.CodeGeneration;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST;
public class InlineArrayProcessor
{
    internal static bool HandleInlineArrayConversionToSpan(IVisitorContext context, string ilVar, ITypeSymbol fromType, SyntaxNode fromNode, OpCode opcode, string name, VariableMemberKind memberKind, string parentName = null)
    {
        int inlineArrayLength = InlineArrayLengthFrom(fromType);
        if (inlineArrayLength == -1)
            return false;
        
        if (!IsNodeUsedToInitializeSpanLocalVariable(context, fromNode)
            && !IsNodeAssignedToLocalVariable(context, fromNode)
            && !IsNodeUsedAsSpanArgument(context, fromNode))
            return false;
        
        // ldloca.s address of fromNode.
        // ldci4 fromNode.Length (size of the inline array)
        context.EmitCilInstruction(ilVar, opcode, context.DefinitionVariables.GetVariable(name, memberKind, parentName).VariableName);
        context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4, inlineArrayLength);
        context.EmitCilInstruction(ilVar, OpCodes.Call, InlineArrayAsSpanMethodFor(context, fromType));
        return true;

        static bool IsNodeAssignedToLocalVariable(IVisitorContext context, SyntaxNode nodeToCheck)
        {
            if (nodeToCheck.Parent is not AssignmentExpressionSyntax assignmentExpression)
                return false;
           
            var lhs = context.SemanticModel.GetSymbolInfo(assignmentExpression.Left);
            if (lhs.Symbol == null)
                return false;

            return SymbolEqualityComparer.Default.Equals(lhs.Symbol.GetMemberType().OriginalDefinition, context.RoslynTypeSystem.SystemSpan);
        }
        
        static bool IsNodeUsedAsSpanArgument(IVisitorContext context, SyntaxNode nodeToCheck)
        {
            if (nodeToCheck.Parent is not ArgumentSyntax argumentSyntax)
                return false;
           
            var argumentIndex = ((ArgumentListSyntax) argumentSyntax.Parent).Arguments.IndexOf(argumentSyntax);
            var invocation = argumentSyntax.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            var associatedParameterSymbol = ((IMethodSymbol) context.SemanticModel.GetSymbolInfo(invocation.Expression).Symbol).Parameters.ElementAtOrDefault(argumentIndex);

            return SymbolEqualityComparer.Default.Equals(associatedParameterSymbol.Type.OriginalDefinition, context.RoslynTypeSystem.SystemSpan);
        }
        
        static bool IsNodeUsedToInitializeSpanLocalVariable(IVisitorContext context, SyntaxNode nodeToCheck)
        {
            var parent = nodeToCheck.Parent;
            if (!parent.IsKind(SyntaxKind.EqualsValueClause) || !parent.Parent.IsKind(SyntaxKind.VariableDeclarator))
                return false;

            var variableDeclaration = (VariableDeclarationSyntax) parent.Parent.Parent!;
            var declaredVariableType = ModelExtensions.GetTypeInfo(context.SemanticModel, variableDeclaration.Type);

            return SymbolEqualityComparer.Default.Equals(declaredVariableType.Type?.OriginalDefinition, context.RoslynTypeSystem.SystemSpan);
        }

        static string InlineArrayAsSpanMethodFor(IVisitorContext context, ITypeSymbol inlineArrayType)
        {
            return PrivateImplementationInlineArrayGenericInstanceMethodFor(
                context,
                PrivateImplementationDetailsGenerator.GetOrEmmitInlineArrayAsSpanMethod(context),
                "InlineArrayAsSpan",
                inlineArrayType);
        }
    }
    
    internal static bool TryHandleIntIndexElementAccess(IVisitorContext context, string ilVar, ElementAccessExpressionSyntax elementAccess, out ITypeSymbol elementType)
    {
        elementType = null;
        if (elementAccess.Expression.IsKind(SyntaxKind.ElementAccessExpression))
            return false;
        
        var inlineArrayType = context.SemanticModel.GetSymbolInfo(elementAccess.Expression).Symbol.EnsureNotNull().GetMemberType();
        if (!inlineArrayType.TryGetAttribute<InlineArrayAttribute>(out _))
            return false;
        
        ExpressionVisitor.Visit(context, ilVar, elementAccess.Expression);
        Debug.Assert(elementAccess.ArgumentList.Arguments.Count == 1);

        var method = string.Empty;
        if (elementAccess.ArgumentList.Arguments[0].Expression.TryGetLiteralValueFor(out int index) && index == 0)
        {
            method = InlineArrayFirstElementRefMethodFor(context, inlineArrayType);
        }
        else
        {
            ExpressionVisitor.Visit(context, ilVar, elementAccess.ArgumentList.Arguments[0].Expression);
            method = InlineArrayElementRefMethodFor(context, inlineArrayType);
        }
        context.EmitCilInstruction(ilVar, OpCodes.Call, method);

        elementType = InlineArrayElementTypeFrom(inlineArrayType);
        return true;
        
        static string InlineArrayFirstElementRefMethodFor(IVisitorContext context, ITypeSymbol inlineArrayType)
        {
            return PrivateImplementationInlineArrayGenericInstanceMethodFor(
                context,
                PrivateImplementationDetailsGenerator.GetOrEmmitInlineArrayFirstElementRefMethod(context),
                "InlineArrayFirstElementRef",
                inlineArrayType);
        }
        
        static string InlineArrayElementRefMethodFor(IVisitorContext context, ITypeSymbol inlineArrayType)
        {
            return PrivateImplementationInlineArrayGenericInstanceMethodFor(
                context,
                PrivateImplementationDetailsGenerator.GetOrEmmitInlineArrayElementRefMethod(context),
                "InlineArrayElementRef",
                inlineArrayType);
        }
    }
    
    private static string PrivateImplementationInlineArrayGenericInstanceMethodFor(IVisitorContext context, string openGenericTypeVar, string methodName, ITypeSymbol inlineArrayType)
    {
        var varName = context.Naming.SyntheticVariable(methodName, ElementKind.GenericInstance);

        var exps = openGenericTypeVar.MakeGenericInstanceMethod(
            varName,
            [
                context.TypeResolver.Resolve(inlineArrayType), // TBuffer 
                context.TypeResolver.Resolve(InlineArrayElementTypeFrom(inlineArrayType)) // TElement 
            ]);

        foreach (var exp in exps)
        {
            context.WriteCecilExpression(exp);
            context.WriteNewLine();
        }

        return varName;
    }

    private static ITypeSymbol InlineArrayElementTypeFrom(ITypeSymbol inlineArrayType)
    {
        return ((IFieldSymbol) inlineArrayType.GetMembers().First()).Type;
    }
    
    private static int InlineArrayLengthFrom(ITypeSymbol rhsType)
    {
        return rhsType.TryGetAttribute<InlineArrayAttribute>(out var inlineArrayAttribute)
            ? (int) inlineArrayAttribute.ConstructorArguments.First().Value
            : -1;
    }
}
