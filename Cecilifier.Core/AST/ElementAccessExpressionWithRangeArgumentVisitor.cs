using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST;

internal class ElementAccessExpressionWithRangeArgumentVisitor : SyntaxWalkerBase
{
    internal ElementAccessExpressionWithRangeArgumentVisitor(IVisitorContext context, string ilVar, ExpressionVisitor expressionVisitor, bool targetAlreadyLoaded = false) : base(context)
    {
        _expressionVisitor = expressionVisitor;
        _targetAlreadyLoaded = targetAlreadyLoaded;
        _ilVar = ilVar;
    }

    public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        using var _ = LineInformationTracker.Track(Context, node);
        if (!_targetAlreadyLoaded)
            node.Expression.Accept(_expressionVisitor);

        var elementAccessExpressionType = Context.SemanticModel.GetTypeInfo(node).Type.EnsureNotNull();
        _targetSpanType = elementAccessExpressionType;
        _spanCopyVariable = CodeGenerationHelpers.StoreTopOfStackInLocalVariable(Context, _ilVar, "localSpanCopy", elementAccessExpressionType).VariableName;
        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloca, _spanCopyVariable);

        node.ArgumentList.Accept(this); // Visit the argument list with ourselves.

        var sliceMethod = elementAccessExpressionType.GetMembers("Slice").OfType<IMethodSymbol>().Single(candidate => candidate.Parameters.Length == 2); // Slice(int, int)
        Context.AddCallToMethod(sliceMethod, _ilVar, MethodDispatchInformation.MostLikelyVirtual);
    }

    // This will handle usages like s[1..^3], i.e, RangeExpressions used in the argument
    public override void VisitRangeExpression(RangeExpressionSyntax node)
    {
        using var __ = LineInformationTracker.Track(Context, node);
        using var _ = Context.WithFlag<ContextFlagReseter>(Constants.ContextFlags.InRangeExpression);

        // Compute range start index
        Utils.EnsureNotNull(node.LeftOperand).Accept(_expressionVisitor);

        var startIndexVar = CodeGenerationHelpers.StoreTopOfStackInLocalVariable(Context, _ilVar, "startIndex", Context.RoslynTypeSystem.SystemInt32).VariableName;

        // Compute number of elements to slice

        // compute range right index.
        Utils.EnsureNotNull(node.RightOperand).Accept(_expressionVisitor);

        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, startIndexVar);
        Context.EmitCilInstruction(_ilVar, OpCodes.Sub);
        
        var elementCountVar = CodeGenerationHelpers.StoreTopOfStackInLocalVariable(Context, _ilVar, "elementCount", Context.RoslynTypeSystem.SystemInt32).VariableName;

        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, startIndexVar);
        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, elementCountVar);
    }

    // This will handle usages like s[r]
    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        ProcessIndexerExpressionWithRangeAsArgument(node);
    }

    // This will handle usages like s[o.r]
    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        ProcessIndexerExpressionWithRangeAsArgument(node);
    }

    private void ProcessIndexerExpressionWithRangeAsArgument(ExpressionSyntax node)
    {
        using var _ = LineInformationTracker.Track(Context, node);
        Context.AddCallToMethod(_targetSpanType.GetMembers().OfType<IPropertySymbol>().Single(p => p.Name == "Length").GetMethod, _ilVar, MethodDispatchInformation.MostLikelyVirtual);
        var spanLengthVar = CodeGenerationHelpers.StoreTopOfStackInLocalVariable(Context, _ilVar, "spanLengthVar", Context.RoslynTypeSystem.SystemInt32).VariableName;

        node.Accept(_expressionVisitor);

        var systemIndex = Context.RoslynTypeSystem.SystemIndex;
        var systemRange = Context.RoslynTypeSystem.SystemRange;
        var rangeVar = CodeGenerationHelpers.StoreTopOfStackInLocalVariable(Context, _ilVar, "rangeVar", systemRange).VariableName;

        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloca, rangeVar);
        Context.AddCallToMethod(systemRange.GetMembers().OfType<IPropertySymbol>().Single(p => p.Name == "Start").GetMethod, _ilVar, MethodDispatchInformation.MostLikelyVirtual);
        var indexVar = CodeGenerationHelpers.StoreTopOfStackInLocalVariable(Context, _ilVar, "index", systemIndex).VariableName;

        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloca, indexVar);
        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, spanLengthVar);
        Context.AddCallToMethod(systemIndex.GetMembers().OfType<IMethodSymbol>().Single(p => p.Name == "GetOffset"), _ilVar, MethodDispatchInformation.MostLikelyVirtual);

        var startIndexVar = CodeGenerationHelpers.StoreTopOfStackInLocalVariable(Context, _ilVar, "startIndex", Context.RoslynTypeSystem.SystemInt32).VariableName;

        // Calculate number of elements to slice.
        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloca, rangeVar);
        Context.AddCallToMethod(systemRange.GetMembers().OfType<IPropertySymbol>().Single(p => p.Name == "End").GetMethod, _ilVar, MethodDispatchInformation.MostLikelyVirtual);
        Context.EmitCilInstruction(_ilVar, OpCodes.Stloc, indexVar);

        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloca, indexVar);
        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, spanLengthVar);
        Context.AddCallToMethod(systemIndex.GetMembers().OfType<IMethodSymbol>().Single(p => p.Name == "GetOffset"), _ilVar, MethodDispatchInformation.MostLikelyVirtual);
        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, startIndexVar);
        Context.EmitCilInstruction(_ilVar, OpCodes.Sub);
        var elementCountVar = CodeGenerationHelpers.StoreTopOfStackInLocalVariable(Context, _ilVar, "elementCount", Context.RoslynTypeSystem.SystemInt32).VariableName;

        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloca, _spanCopyVariable);
        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, startIndexVar);
        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, elementCountVar);
    }

    private readonly ExpressionVisitor _expressionVisitor;
    private readonly bool _targetAlreadyLoaded;
    private string _spanCopyVariable;
    private readonly string _ilVar;
    private ITypeSymbol _targetSpanType; // Span<T> in which indexer is being invoked
}
