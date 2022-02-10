using Cecilifier.Core.Mappings;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST;

internal static class CecilExpressionFactory
{
    public static void EmitThrow(IVisitorContext context, string ilVar, ExpressionSyntax expression)
    {
        var _ = LineInformationTracker.Track(context, expression);
        ExpressionVisitor.Visit(context, ilVar, expression);
        context.EmitCilInstruction(ilVar, OpCodes.Throw);
    }
}
