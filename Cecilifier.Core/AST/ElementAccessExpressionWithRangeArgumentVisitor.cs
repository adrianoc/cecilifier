using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Variables;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST;

internal class ElementAccessExpressionWithRangeArgumentVisitor : SyntaxWalkerBase
{
    internal ElementAccessExpressionWithRangeArgumentVisitor(IVisitorContext context, string ilVar, ExpressionVisitor expressionVisitor) : base(context)
    {
        _expressionVisitor = expressionVisitor;
        _ilVar = ilVar;
    }

    public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        using var _ = LineInformationTracker.Track(Context, node);
        node.Expression.Accept(_expressionVisitor);

        var elementAccessExpressionType = Context.SemanticModel.GetTypeInfo(node.Expression).Type.EnsureNotNull();
        _targetSpanType = elementAccessExpressionType;
        DefinitionVariable methodVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        string resolvedVarType = Context.TypeResolver.Resolve(elementAccessExpressionType);
        _spanCopyVariable = AddLocalVariableWithResolvedType("localSpanCopy", methodVar, resolvedVarType).VariableName;
        Context.EmitCilInstruction(_ilVar, OpCodes.Stloc, _spanCopyVariable);
        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloca, _spanCopyVariable);

        node.ArgumentList.Accept(this); // Visit the argument list with ourselves.

        var sliceMethod = elementAccessExpressionType.GetMembers("Slice").OfType<IMethodSymbol>().Single(candidate => candidate.Parameters.Length == 2); // Slice(int, int)
        AddMethodCall(_ilVar, sliceMethod);
    }

    // This will handle usages like s[1..^3], i.e, RangeExpressions used in the argument
    public override void VisitRangeExpression(RangeExpressionSyntax node)
    {
        using var __ = LineInformationTracker.Track(Context, node);
        using var _ = Context.WithFlag(Constants.ContextFlags.InRangeExpression);

        Utils.EnsureNotNull(node.LeftOperand);
        Utils.EnsureNotNull(node.RightOperand);

        // Compute range start index
        node.LeftOperand.Accept(_expressionVisitor);
        DefinitionVariable methodVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        string resolvedVarType = Context.TypeResolver.Bcl.System.Int32;
        var startIndexVar = AddLocalVariableWithResolvedType("startIndex", methodVar, resolvedVarType).VariableName;
        Context.EmitCilInstruction(_ilVar, OpCodes.Stloc, startIndexVar);

        // Compute number of elements to slice

        // compute range right index.
        node.RightOperand.Accept(_expressionVisitor);

        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, startIndexVar);
        Context.EmitCilInstruction(_ilVar, OpCodes.Sub);
        DefinitionVariable methodVar1 = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        string resolvedVarType1 = Context.TypeResolver.Bcl.System.Int32;
        var elementCountVar = AddLocalVariableWithResolvedType("elementCount", methodVar1, resolvedVarType1).VariableName;
        Context.EmitCilInstruction(_ilVar, OpCodes.Stloc, elementCountVar);

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
        AddMethodCall(_ilVar, _targetSpanType.GetMembers().OfType<IPropertySymbol>().Single(p => p.Name == "Length").GetMethod);
        DefinitionVariable methodVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        string resolvedVarType = Context.TypeResolver.Bcl.System.Int32;
        var spanLengthVar = AddLocalVariableWithResolvedType("spanLengthVar", methodVar, resolvedVarType).VariableName;
        Context.EmitCilInstruction(_ilVar, OpCodes.Stloc, spanLengthVar);

        node.Accept(_expressionVisitor);

        var systemIndex = Context.RoslynTypeSystem.SystemIndex;
        var systemRange = Context.RoslynTypeSystem.SystemRange;
        DefinitionVariable methodVar1 = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        string resolvedVarType1 = Context.TypeResolver.Resolve(systemRange);
        var rangeVar = AddLocalVariableWithResolvedType("rangeVar", methodVar1, resolvedVarType1).VariableName;
        Context.EmitCilInstruction(_ilVar, OpCodes.Stloc, rangeVar);

        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloca, rangeVar);
        AddMethodCall(_ilVar, systemRange.GetMembers().OfType<IPropertySymbol>().Single(p => p.Name == "Start").GetMethod);
        DefinitionVariable methodVar2 = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        string resolvedVarType2 = Context.TypeResolver.Resolve(systemIndex);
        var indexVar = AddLocalVariableWithResolvedType("index", methodVar2, resolvedVarType2).VariableName;
        Context.EmitCilInstruction(_ilVar, OpCodes.Stloc, indexVar);

        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloca, indexVar);
        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, spanLengthVar);
        AddMethodCall(_ilVar, systemIndex.GetMembers().OfType<IMethodSymbol>().Single(p => p.Name == "GetOffset"));

        DefinitionVariable methodVar3 = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        string resolvedVarType3 = Context.TypeResolver.Bcl.System.Int32;
        var startIndexVar = AddLocalVariableWithResolvedType("startIndex", methodVar3, resolvedVarType3).VariableName;
        Context.EmitCilInstruction(_ilVar, OpCodes.Stloc, startIndexVar);

        // Calculate number of elements to slice.
        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloca, rangeVar);
        AddMethodCall(_ilVar, systemRange.GetMembers().OfType<IPropertySymbol>().Single(p => p.Name == "End").GetMethod);
        Context.EmitCilInstruction(_ilVar, OpCodes.Stloc, indexVar);

        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloca, indexVar);
        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, spanLengthVar);
        AddMethodCall(_ilVar, systemIndex.GetMembers().OfType<IMethodSymbol>().Single(p => p.Name == "GetOffset"));
        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, startIndexVar);
        Context.EmitCilInstruction(_ilVar, OpCodes.Sub);
        DefinitionVariable methodVar4 = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Method);
        string resolvedVarType4 = Context.TypeResolver.Bcl.System.Int32;
        var elementCountVar = AddLocalVariableWithResolvedType("elementCount", methodVar4, resolvedVarType4).VariableName;
        Context.EmitCilInstruction(_ilVar, OpCodes.Stloc, elementCountVar);

        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloca, _spanCopyVariable);
        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, startIndexVar);
        Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, elementCountVar);
    }

    private readonly ExpressionVisitor _expressionVisitor;
    private string _spanCopyVariable;
    private readonly string _ilVar;
    private ITypeSymbol _targetSpanType; // Span<T> in which indexer is being invoked
}
