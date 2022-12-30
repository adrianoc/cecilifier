using System;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.AST;

internal class BinaryOperatorHandler
{
    private readonly Action<IVisitorContext, string, ITypeSymbol, ITypeSymbol> _action;
    public bool VisitRightOperand { get; }

    public virtual void Process(IVisitorContext context, string ilVar, ITypeSymbol leftType, ITypeSymbol rightType)
    {
        _action(context, ilVar, leftType, rightType);
    }

    public BinaryOperatorHandler(Action<IVisitorContext, string, ITypeSymbol, ITypeSymbol> action, bool visitRightOperand = true)
    {
        _action = action;
        VisitRightOperand = visitRightOperand;
    }
}
