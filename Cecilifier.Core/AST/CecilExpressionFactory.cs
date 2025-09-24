using System.Reflection.Emit;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core.Mappings;

namespace Cecilifier.Core.AST;

internal static class CecilExpressionFactory
{
    public static void EmitThrow(IVisitorContext context, string ilVar, ExpressionSyntax expression)
    {
        _ = LineInformationTracker.Track(context, expression);
        ExpressionVisitor.Visit(context, ilVar, expression);
        context.ApiDriver.WriteCilInstruction(context, ilVar, OpCodes.Throw);
    }
}
