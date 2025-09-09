using System.Linq;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Cecilifier.Core.CodeGeneration;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;

namespace Cecilifier.Core.AST;

public class ArrayInitializationProcessor
{
    /// <summary>
    /// Generates Cecil calls to initialize an array which reference is at the top of the stack.
    /// </summary>
    /// <param name="visitor">An <see cref="ExpressionVisitor"/> used to visit each value to be stored in the array</param>
    /// <param name="elementType"></param>
    /// <param name="elements">Values to be stored.</param>
    /// <param name="parentOperation">A operation conveying information on potential conversions.</param>
    /// <typeparam name="TElement">The type of the ast node representing the elements.</typeparam>
    internal static void InitializeUnoptimized<TElement>(ExpressionVisitor visitor, ITypeSymbol elementType, SeparatedSyntaxList<TElement>? elements, IOperation parentOperation = null) where TElement : CSharpSyntaxNode
    {
        var context = visitor.Context;
        var stelemOpCode = elementType.StelemOpCode();
        var resolvedElementType = context.TypeResolver.ResolveAny(elementType);

        for (var i = 0; i < elements?.Count; i++)
        {
            context.EmitCilInstruction(visitor.ILVariable, OpCodes.Dup);
            context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldc_I4, i);
            elements.Value[i].Accept(visitor);

            var operation = GetConversionOperation(parentOperation, i);
            context.TryApplyConversions(visitor.ILVariable, operation);
            context.EmitCilInstruction(visitor.ILVariable, stelemOpCode, stelemOpCode == OpCodes.Stelem ? resolvedElementType : null);
        }
        IConversionOperation GetConversionOperation(IOperation operation, int index)
        {
            return operation switch
            {
                ICollectionExpressionOperation ceo => ceo.Elements[index],
                IArrayInitializerOperation initializerOperation => initializerOperation.ElementValues[index], 
                _ => null
            } as IConversionOperation;
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
            elementType.SizeofPrimitiveType(),
            elements.Select(item => item.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>().Single().Token.ValueText).ToArray(),
            StringToSpanOfBytesConverters.For(elementType.FullyQualifiedName()));

        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Dup);
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldtoken, backingFieldVar);
        context.EmitCilInstruction(visitor.ILVariable, OpCodes.Call, initializeArrayHelper);
    }
}

