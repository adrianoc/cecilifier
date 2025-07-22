using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Cecilifier.Core.CodeGeneration;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST;

internal static class CollectionExpressionProcessor
{
    public static void Process(ExpressionVisitor visitor, CollectionExpressionSyntax node)
    {
        var targetTypeSymbol = visitor.Context.GetTypeInfo(node).ConvertedType.EnsureNotNull();
        if (targetTypeSymbol is IArrayTypeSymbol arrayType)
        {
            HandleAssignmentToArray(visitor, node, arrayType);
        }
        else if (SymbolEqualityComparer.Default.Equals(targetTypeSymbol.OriginalDefinition, visitor.Context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(List<>).FullName!)))
        {
            HandleAssignmentToList(visitor, node, (INamedTypeSymbol) targetTypeSymbol);
        }
        else
        {
            HandleAssignmentToSpan(visitor, node, (INamedTypeSymbol) targetTypeSymbol);
        }
    }

    private static void HandleAssignmentToList(ExpressionVisitor visitor, CollectionExpressionSyntax node, INamedTypeSymbol listOfTTypeSymbol)
    {
        var context = visitor.Context;
        var collectionExpressionOperation = context.SemanticModel.GetOperation(node).EnsureNotNull<IOperation, ICollectionExpressionOperation>();
        var (spanToList, resolvedListTypeArgument) = CecilDefinitionsFactory.Collections.InstantiateListToStoreElements(context, visitor.ILVariable, listOfTTypeSymbol, collectionExpressionOperation.Elements.Length);
        
        context.WriteNewLine();
        context.WriteComment($"Initialize each list element through the span (variable '{spanToList.VariableName}')");
        var index = 0;
        var spanGetItemMethod = CecilDefinitionsFactory.Collections.GetSpanIndexerGetter(context, resolvedListTypeArgument);
        var stindOpCode = listOfTTypeSymbol.TypeArguments[0].StindOpCodeFor();
        var targetElementType = stindOpCode == OpCodes.Stobj ? resolvedListTypeArgument : null; // Stobj expects the type of the object being stored.
        
        foreach (var element in node.Elements)
        {
            context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldloca_S, spanToList.VariableName);
            context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldc_I4, index);
            context.EmitCilInstruction(visitor.ILVariable, OpCodes.Call, spanGetItemMethod);
            visitor.Visit(element);
            context.TryApplyConversions(visitor.ILVariable, collectionExpressionOperation.Elements[index]);
            context.EmitCilInstruction(visitor.ILVariable, stindOpCode, targetElementType);
            index++;
        }
    }
   
    private static void HandleAssignmentToSpan(ExpressionVisitor visitor, CollectionExpressionSyntax node, INamedTypeSymbol spanTypeSymbol)
    {
        Debug.Assert(SymbolEqualityComparer.Default.Equals(spanTypeSymbol.OriginalDefinition, visitor.Context.RoslynTypeSystem.SystemSpan));
     
        var context = visitor.Context;
        var inlineArrayVar = GetOrEmitSyntheticInlineArrayFor(node, context);
        
        var currentMethodVar = context.DefinitionVariables.GetLastOf(VariableMemberKind.Method).VariableName;
        var inlineArrayElementType = spanTypeSymbol.TypeArguments[0];
        var inlineArrayLocalVar = context.Naming.SyntheticVariable("buffer", ElementKind.LocalVariable);
        var inlineArrayTypeVar = inlineArrayVar.MakeGenericInstanceType(context.TypeResolver.Resolve(inlineArrayElementType));
        context.WriteCecilExpression($"var {inlineArrayLocalVar} = {CecilDefinitionsFactory.LocalVariable(inlineArrayTypeVar)};\n");
        context.WriteCecilExpression($"{currentMethodVar}.Body.Variables.Add({inlineArrayLocalVar});\n");
        
        // Initializes the inline array
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldloca_S, inlineArrayLocalVar);
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Initobj, inlineArrayTypeVar);

        var openInlineArrayElementRef = PrivateImplementationDetailsGenerator
            .GetOrEmmitInlineArrayElementRefMethod(context);
        var inlineArrayElementRefMethodVar = openInlineArrayElementRef
            .VariableName
            .MakeGenericInstanceMethod(context, openInlineArrayElementRef.MemberName, [$"{inlineArrayLocalVar}.VariableType", context.TypeResolver.Resolve(spanTypeSymbol.TypeArguments[0])]);
        
        var storeOpCode = inlineArrayElementType.StindOpCodeFor();
        var targetElementType = storeOpCode == OpCodes.Stobj ? context.TypeResolver.Resolve(inlineArrayElementType) : null; // Stobj expects the type of the object being stored.
        var collectionExpressionOperation = context.SemanticModel.GetOperation(node).EnsureNotNull<IOperation, ICollectionExpressionOperation>();
        var index = 0;
        foreach (var element in node.Elements)
        {
            context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldloca_S, inlineArrayLocalVar);
            context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldc_I4, index);
            context.EmitCilInstruction(visitor.ILVariable, OpCodes.Call, inlineArrayElementRefMethodVar);
            visitor.Visit(element);
            context.TryApplyConversions(visitor.ILVariable, collectionExpressionOperation.Elements[index]);
            context.EmitCilInstruction(visitor.ILVariable, storeOpCode, targetElementType);
            index++;
        }
        
        // convert the initialized InlineArray to a span and put it in the stack.
        var openInlineArrayAsSpanVar = PrivateImplementationDetailsGenerator.GetOrEmmitInlineArrayAsSpanMethod(context);
        var inlineArrayAsSpanMethodVar = openInlineArrayAsSpanVar
                                            .VariableName
                                            .MakeGenericInstanceMethod(context, openInlineArrayAsSpanVar.MemberName, [$"{inlineArrayLocalVar}.VariableType", context.TypeResolver.Resolve(spanTypeSymbol.TypeArguments[0])]);
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldloca_S, inlineArrayLocalVar);
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldc_I4, node.Elements.Count);
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Call, inlineArrayAsSpanMethodVar);
    }

    private static string GetOrEmitSyntheticInlineArrayFor(CollectionExpressionSyntax node, IVisitorContext context)
    {
        return InlineArrayGenerator.GetOrGenerateInlineArrayType(context, node.Elements.Count, $"Declares an inline array for backing the data for the collection expression: {node.SourceDetails()}");
    }

    private static void HandleAssignmentToArray(ExpressionVisitor visitor, CollectionExpressionSyntax node, IArrayTypeSymbol arrayTypeSymbol)
    {
        visitor.Context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldc_I4, node.Elements.Count);
        visitor.Context.EmitCilInstruction(visitor.ILVariable, OpCodes.Newarr, visitor.Context.TypeResolver.Resolve(arrayTypeSymbol.ElementType));
            
        if (PrivateImplementationDetailsGenerator.IsApplicableTo(node, visitor.Context))
            ArrayInitializationProcessor.InitializeOptimized(visitor, arrayTypeSymbol.ElementType, node.Elements);
        else
            ArrayInitializationProcessor.InitializeUnoptimized<CollectionElementSyntax>(visitor, arrayTypeSymbol.ElementType, node.Elements, visitor.Context.SemanticModel.GetOperation(node));
    }
}
