#nullable enable
using System.Diagnostics;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST.Params;

internal class ArrayExpandedParamsArgumentHandler : ExpandedParamsArgumentHandler
{
    public ArrayExpandedParamsArgumentHandler(IVisitorContext context, IParameterSymbol paramsParameter, ArgumentListSyntax argumentList, string ilVar) : base(context, paramsParameter, argumentList, ilVar)
    {
        _currentIndex = 0;
        _stelemOpCode = ElementType.StelemOpCode();
        
        _backingVariableName = Context.AddLocalVariableToCurrentMethod($"{paramsParameter.Name}Params", Context.TypeResolver.Resolve(paramsParameter.Type));
        
        var paramsType = Context.TypeResolver.Resolve(ElementType);
        Context.EmitCilInstruction(ilVar, OpCodes.Ldc_I4, ElementCount);
        Context.EmitCilInstruction(ilVar, OpCodes.Newarr, paramsType);
        Context.EmitCilInstruction(ilVar, OpCodes.Stloc, _backingVariableName);
    }

    private OpCode _stelemOpCode;
    private string _backingVariableName;

    /// <summary>
    /// Callback used to pre-process argument handling. It can be used to inject code
    /// before the code generation for each argument.
    /// </summary>
    /// <param name="argument">Argument being processed.</param>
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

    /// <summary>
    /// Callback used to post-process argument handling. It can be used to inject code
    /// *after* generating code for each attribute.
    /// </summary>
    /// <param name="argument">Argument being processed.</param>
    internal override void PostProcessArgument(ArgumentSyntax argument)
    {
        var argumentIndex = ParentArgumentList.Arguments.IndexOf(argument);
        Debug.Assert(argumentIndex >= 0);

        if (argumentIndex < FirstArgumentIndex)
            return;
                
        Context.EmitCilInstruction(ilVar, _stelemOpCode);
    }
}
