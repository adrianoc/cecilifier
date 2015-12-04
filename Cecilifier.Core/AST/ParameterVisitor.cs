using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
	class ParameterVisitor : SyntaxWalkerBase
	{
		internal ParameterVisitor(IVisitorContext ctx) : base(ctx)
		{
		}

		public override void VisitIdentifierName(IdentifierNameSyntax node)
		{
			symbol = Context.SemanticModel.GetSymbolInfo(node).Symbol as IParameterSymbol;
		}

		public static IParameterSymbol Process(IVisitorContext context, ExpressionSyntax node)
		{
			var visitor = new ParameterVisitor(context);
			visitor.Visit(node);

			return visitor.symbol;
		}

		private IParameterSymbol symbol;
	}
}
