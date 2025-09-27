#nullable enable
using System.Diagnostics;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver;
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
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldc_I4, ElementCount);
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Newarr, paramsType);
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Stloc, new CilLocalVariableHandle(_backingVariableName));
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
            Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldloc, _backingVariableName);
        }
                
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Dup);
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldc_I4, _currentIndex++);
    }

    internal override void PostProcessArgument(ArgumentSyntax argument)
    {
        var argumentIndex = ParentArgumentList.Arguments.IndexOf(argument);
        Debug.Assert(argumentIndex >= 0);

        if (argumentIndex < FirstArgumentIndex)
            return;
                
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, _stelemOpCode);
    }
}
