using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST.Params;

internal class ListBackedExpandedParamsArgumentHandler : ExpandedParamsArgumentHandler
{
    private readonly DefinitionVariable spanWrappingListVariable;
    private int index;
    private readonly OpCode stIndOpCodeToUse;
    private readonly string targetElementTypeWhenStoring;
    private readonly string spanIndexerGetter;
    
    public ListBackedExpandedParamsArgumentHandler(ExpressionVisitor expressionVisitor, IParameterSymbol paramsParameter, ArgumentListSyntax argumentList) : base(expressionVisitor.Context, paramsParameter, argumentList, expressionVisitor.ILVariable)
    {
        var elements = argumentList.Arguments.Select(arg => Context.SemanticModel.GetOperation(arg.Expression));
        var elementType = ((INamedTypeSymbol) paramsParameter.Type).TypeArguments[0];
        var openListType = Context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(List<>).FullName!).EnsureNotNull();
        (spanWrappingListVariable, var resolvedListTypeArgument) = CecilDefinitionsFactory.Collections.InstantiateListToStoreElements(expressionVisitor.Context, expressionVisitor.ILVariable, openListType.Construct(elementType), elements.Count());
        
        Context.WriteNewLine();
        Context.WriteComment($"Initialize each list element through the span (variable '{spanWrappingListVariable.VariableName}')");
        stIndOpCodeToUse = elementType.StindOpCodeFor();
        targetElementTypeWhenStoring = stIndOpCodeToUse == OpCodes.Stobj ? resolvedListTypeArgument : null; // Stobj expects the type of the object being stored.

        spanIndexerGetter = CecilDefinitionsFactory.Collections.GetSpanIndexerGetter(Context, resolvedListTypeArgument);
    }

    internal override void PreProcessArgument(ArgumentSyntax argument)
    {
        Context.EmitCilInstruction(ilVar, OpCodes.Ldloca_S, spanWrappingListVariable.VariableName);
        Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4, index++);
        Context.EmitCilInstruction(ilVar, OpCodes.Call, spanIndexerGetter);
    }

    internal override void PostProcessArgument(ArgumentSyntax argument)
    { 
        Context.EmitCilInstruction(ilVar, stIndOpCodeToUse, targetElementTypeWhenStoring);
    }
}
