#nullable enable
using System.Diagnostics;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core.Extensions;

namespace Cecilifier.Core.AST.Params;

internal class ArrayExpandedParamsArgumentHandler : ExpandedParamsArgumentHandler
{
    public ArrayExpandedParamsArgumentHandler(IVisitorContext context, IParameterSymbol paramsParameter, ArgumentListSyntax argumentList, string ilVar) : base(context, paramsParameter, argumentList, ilVar)
    {
        _currentIndex = 0;
        _stelemOpCode = ElementType.StelemOpCode();
        
        _backingVariableName = Context.AddLocalVariableToCurrentMethod($"{paramsParameter.Name}Params", Context.TypeResolver.ResolveAny(paramsParameter.Type));
        
        var paramsType = Context.TypeResolver.ResolveAny(ElementType);
        Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4, ElementCount);
        Context.EmitCilInstruction(ilVar, OpCodes.Newarr, paramsType);
        Context.EmitCilInstruction(ilVar, OpCodes.Stloc, _backingVariableName);
    }

    private OpCode _stelemOpCode;
    private string _backingVariableName;

    internal override void PreProcessArgument(ArgumentSyntax argument)
    {
        var argumentIndex = ParentArgumentList.Arguments.IndexOf(argument);
        Debug.Assert(argumentIndex >= 0);

        if (argumentIndex < FirstArgumentIndex)
            return;
                
        if (argumentIndex == FirstArgumentIndex)
        {
            Context.EmitCilInstruction(ilVar, OpCodes.Ldloc, _backingVariableName);
        }
                
        Context.EmitCilInstruction(ilVar, OpCodes.Dup);
        Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4, _currentIndex++);
    }

    internal override void PostProcessArgument(ArgumentSyntax argument)
    {
        var argumentIndex = ParentArgumentList.Arguments.IndexOf(argument);
        Debug.Assert(argumentIndex >= 0);

        if (argumentIndex < FirstArgumentIndex)
            return;
                
        Context.EmitCilInstruction(ilVar, _stelemOpCode);
    }
}
