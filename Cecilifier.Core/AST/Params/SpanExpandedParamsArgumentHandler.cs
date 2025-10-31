using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core.CodeGeneration;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;

#nullable enable

namespace Cecilifier.Core.AST.Params;

internal class SpanExpandedParamsArgumentHandler : ExpandedParamsArgumentHandler
{
    private readonly string _inlineArrayVariableName;
    private readonly OpCode _stindOpCode;
    private readonly string _inlineArrayType;
    private readonly ITypeSymbol _paramsParameterType;

    public SpanExpandedParamsArgumentHandler(IVisitorContext context, IParameterSymbol paramsParameter, ArgumentListSyntax argumentList, string ilVar) : base(context, paramsParameter, argumentList, ilVar)
    {
        _paramsParameterType = paramsParameter.Type.ElementTypeSymbolOf();
        _stindOpCode = _paramsParameterType.StindOpCodeFor();
        
        var openInlineArrayType = InlineArrayGenerator.GetOrGenerateInlineArrayType(context, argumentList.Arguments.Count, "InlineArray to store the `params` values.");
        _inlineArrayType = new ResolvedType(openInlineArrayType).MakeGenericInstanceType([context.TypeResolver.ResolveAny(_paramsParameterType)]);

        var inlineArrayBuffer = context.AddLocalVariableToCurrentMethod($"{paramsParameter.Name}Arg", _inlineArrayType);
        _inlineArrayVariableName = inlineArrayBuffer.VariableName;
        
        context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldloca_S, _inlineArrayVariableName);
        context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Initobj, _inlineArrayType);
    }

    internal override void PreProcessArgument(ArgumentSyntax argument)
    {
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldloca_S, _inlineArrayVariableName);
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldc_I4, _currentIndex++);
        var openInlineArrayElementRefMethod = PrivateImplementationDetailsGenerator.GetOrEmmitInlineArrayElementRefMethod(Context);

        Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Call, MakeGenericInstanceMethod(openInlineArrayElementRefMethod));
    }

    internal override void PostProcessArgument(ArgumentSyntax argument)
    {
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, _stindOpCode);
    }

    public override void PostProcessArgumentList(ArgumentListSyntax argumentList)
    {
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldloca_S, _inlineArrayVariableName);
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldc_I4, ElementCount);
        
        // gets a Span<T> from the inline array
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Call, MakeGenericInstanceMethod(GetInlineArrayToSpanResolvedMethod()));
    }

    protected virtual DefinitionVariable GetInlineArrayToSpanResolvedMethod() => PrivateImplementationDetailsGenerator.GetOrEmmitInlineArrayAsSpanMethod(Context);
    
    string MakeGenericInstanceMethod(DefinitionVariable genericMethodVariable)
    {
        return genericMethodVariable.VariableName.MakeGenericInstanceMethod(Context, genericMethodVariable.MemberName, [ _inlineArrayType, Context.TypeResolver.ResolveAny(_paramsParameterType)]);
    }
}
