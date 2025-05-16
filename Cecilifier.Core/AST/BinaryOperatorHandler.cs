using System;
using System.Diagnostics.CodeAnalysis;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

#nullable enable

namespace Cecilifier.Core.AST;

internal sealed class BinaryOperatorHandler
{
    private readonly Action<IVisitorContext,string,BinaryExpressionSyntax, ExpressionVisitor>? _handler;
    private readonly Action<IVisitorContext, string, ITypeSymbol?, ITypeSymbol?>? _rawHandler;
    private readonly bool _visitRightOperand;

    public void Process(IVisitorContext context, string ilVar, BinaryExpressionSyntax binaryExpression, ExpressionVisitor visitor)
    {
        if (_handler != null)
        {
            _handler(context, ilVar, binaryExpression, visitor);
        }
        else if (_rawHandler != null)
        {
            ThrowIfVisitorIsNull();
            binaryExpression.Left.Accept(visitor);
            binaryExpression.Left.InjectRequiredConversions(context, ilVar);

            if (_visitRightOperand)
            {
                binaryExpression.Right.Accept(visitor);
                binaryExpression.Right.InjectRequiredConversions(context, ilVar);
            }
            
            ProcessRaw(context, ilVar, binaryExpression.Left, binaryExpression.Right);
        }

        [ExcludeFromCodeCoverage]
        void ThrowIfVisitorIsNull()
        {
            if (visitor == null) throw new InvalidOperationException();
        }
    }
    
    public void ProcessRaw(IVisitorContext context, string ilVar, ExpressionSyntax left, ExpressionSyntax right)
    {
        if (_rawHandler == null) 
            throw new InvalidOperationException("The constructor taking two ITypeSymbols should be used to initialize this instance.");
        
        _rawHandler(
            context, 
            ilVar, 
            context.SemanticModel.GetTypeInfo(left).Type, 
            context.SemanticModel.GetTypeInfo(right).Type);
    }

    public BinaryOperatorHandler(Action<IVisitorContext, string, BinaryExpressionSyntax, ExpressionVisitor> handler)
    {
        _handler = handler;
        _visitRightOperand = true;
    }

    private BinaryOperatorHandler(Action<IVisitorContext, string, ITypeSymbol, ITypeSymbol> action, bool visitRightOperand)
    {
        _rawHandler = action!;
        _visitRightOperand = visitRightOperand;
    }
    
    public static BinaryOperatorHandler Raw(Action<IVisitorContext, string, ITypeSymbol, ITypeSymbol> action, bool visitRightOperand = true) => new(action, visitRightOperand);
}
