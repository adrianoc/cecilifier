using Cecilifier.Core.CodeGeneration;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST;

internal class CollectionExpressionProcessor
{
    public static void Process(ExpressionVisitor visitor, CollectionExpressionSyntax node)
    {
        visitor.Context.EmitCilInstruction(visitor.ILVariable, OpCodes.Ldc_I4, node.Elements.Count);
        
        var arrayTypeSymbol = visitor.Context.GetTypeInfo(node).ConvertedType.EnsureNotNull<ITypeSymbol, IArrayTypeSymbol>();
        visitor.Context.EmitCilInstruction(visitor.ILVariable, OpCodes.Newarr, visitor.Context.TypeResolver.Resolve(arrayTypeSymbol.ElementType));
            
        using var _ = LineInformationTracker.Track(visitor.Context, node);
        if (PrivateImplementationDetailsGenerator.IsApplicableTo(node))
            ArrayInitializationProcessor.InitializeOptimized(visitor, arrayTypeSymbol.ElementType, node.Elements);
        else
            ArrayInitializationProcessor.InitializeUnoptimized<CollectionElementSyntax>(visitor, arrayTypeSymbol.ElementType, node.Elements);
    }
}
