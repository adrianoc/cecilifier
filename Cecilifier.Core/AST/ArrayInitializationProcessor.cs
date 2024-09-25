using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil.Cil;

using Cecilifier.Core.CodeGeneration;
using Cecilifier.Core.Extensions;

namespace Cecilifier.Core.AST;

public class ArrayInitializationProcessor
{
    internal static void InitializeUnoptimized<TElement>(ExpressionVisitor visitor, ITypeSymbol elementType, SeparatedSyntaxList<TElement>? elements) where TElement : CSharpSyntaxNode
    {
        var context = visitor.Context;
        var stelemOpCode = elementType.StelemOpCode();
        for (var i = 0; i < elements?.Count; i++)
        {
            context.EmitCilInstruction(visitor.ILVariable, OpCodes.Dup);
            context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldc_I4, i);
            elements.Value[i].Accept(visitor);

            var itemType = context.SemanticModel.GetTypeInfo(elements.Value[i]);
            if (elementType.IsReferenceType && itemType.Type != null && itemType.Type.IsValueType)
            {
                context.EmitCilInstruction(visitor.ILVariable, OpCodes.Box, context.TypeResolver.Resolve(itemType.Type));
            }

            context.EmitCilInstruction(visitor.ILVariable, stelemOpCode, stelemOpCode == OpCodes.Stelem_Any ? context.TypeResolver.Resolve(elementType) : null);
        }
    }

    internal static void InitializeOptimized<TElement>(ExpressionVisitor visitor, ITypeSymbol elementType, SeparatedSyntaxList<TElement> elements) where TElement : SyntaxNode
    {
        var context = visitor.Context;
        var initializeArrayHelper = context.RoslynTypeSystem.SystemRuntimeCompilerServicesRuntimeHelpers
                                            .GetMembers(Constants.Common.RuntimeHelpersInitializeArrayMethodName)
                                            .Single()
                                            .EnsureNotNull<ISymbol, IMethodSymbol>()
                                            .MethodResolverExpression(context);

        //IL_0006: dup
        //IL_0007: ldtoken field valuetype '<PrivateImplementationDetails>'/'__StaticArrayInitTypeSize=24' '<PrivateImplementationDetails>'::'5BC33F8E8CDE3A32E1CF1EE1B1771AC0400514A8675FC99966FCAE1E8184FDFE'
        //IL_000c: call void [System.Runtime]System.Runtime.CompilerServices.RuntimeHelpers::InitializeArray(class [System.Runtime]System.Array, valuetype [System.Runtime]System.RuntimeFieldHandle)
        //IL_0011: pop
        var backingFieldVar = PrivateImplementationDetailsGenerator.GetOrCreateInitializationBackingFieldVariableName(
            context,
            elementType.SizeofArrayLikeItemElement() * elements.Count,
            elementType.Name,
            $"new {elementType.Name}[] {{ {elements.ToFullString()}}}");

        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Dup);
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldtoken, backingFieldVar);
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Call, initializeArrayHelper);
    }
}
