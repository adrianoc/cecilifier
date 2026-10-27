using System.Collections.Generic;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver.Handles;
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
    private readonly ResolvedType _inlineArrayType;
    private readonly ResolvedType _inlineArrayTypeAsGenericParameter;
    private readonly ITypeSymbol _paramsParameterType;

    public SpanExpandedParamsArgumentHandler(IVisitorContext context, IParameterSymbol paramsParameter, ArgumentListSyntax argumentList, IlContext ilVar) : base(context, paramsParameter, argumentList, ilVar)
    {
        _paramsParameterType = paramsParameter.Type.ElementTypeSymbolOf();
        _stindOpCode = _paramsParameterType.StindOpCodeFor();
        
        var (openInlineArrayType, _) = InlineArrayGenerator.GetOrGenerateInlineArrayType(context, argumentList.Arguments.Count, "InlineArray to store the `params` values.");
        
        _inlineArrayType = context.TypeResolver.MakeGenericInstanceType("typeName???", openInlineArrayType,  [context.TypeResolver.Resolve(_paramsParameterType, ResolveTargetKind.None)], new TypeResolutionContext(ResolveTargetKind.Instruction, TypeResolutionOptions.IsValueType));
        _inlineArrayTypeAsGenericParameter = context.TypeResolver.MakeGenericInstanceType("typeName???", openInlineArrayType,  [context.TypeResolver.Resolve(_paramsParameterType, ResolveTargetKind.None)], new TypeResolutionContext(ResolveTargetKind.GenericTypeArgument, TypeResolutionOptions.IsValueType));
        var inlineArrayTypeForLocalVariables = context.TypeResolver.MakeGenericInstanceType("typeName???", openInlineArrayType,  [context.TypeResolver.Resolve(_paramsParameterType, ResolveTargetKind.None)], new TypeResolutionContext(ResolveTargetKind.LocalVariable, TypeResolutionOptions.IsValueType));

        var inlineArrayBuffer = context.AddLocalVariableToCurrentMethod($"{paramsParameter.Name}Arg", inlineArrayTypeForLocalVariables);
        _inlineArrayVariableName = inlineArrayBuffer.VariableName;
        
        context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Ldloca_S, _inlineArrayVariableName.AsLocalVariable());
        context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Initobj, _inlineArrayType.AsToken());
    }

    internal override void PreProcessArgument(ArgumentSyntax argument)
    {
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldloca_S, _inlineArrayVariableName.AsLocalVariable());
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
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldloca_S, _inlineArrayVariableName.AsLocalVariable());
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldc_I4, ElementCount);
        
        // gets a Span<T> from the inline array
        Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Call, MakeGenericInstanceMethod(GetInlineArrayToSpanResolvedMethod()));
    }

    protected virtual DefinitionVariable GetInlineArrayToSpanResolvedMethod() => PrivateImplementationDetailsGenerator.GetOrEmmitInlineArrayAsSpanMethod(Context);
    
    CilToken MakeGenericInstanceMethod(DefinitionVariable genericMethodVariable)
    {
        IReadOnlyList<ResolvedType> resolvedTypeArguments = [ _inlineArrayTypeAsGenericParameter, Context.TypeResolver.Resolve(_paramsParameterType, ResolveTargetKind.GenericTypeArgument)];
        return Context.MemberResolver.MakeGeneticInstanceMethod(genericMethodVariable.VariableName, genericMethodVariable.MemberName, resolvedTypeArguments).AsToken();
    }
}
