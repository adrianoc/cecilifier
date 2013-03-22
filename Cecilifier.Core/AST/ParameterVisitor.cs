using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
	class ParameterVisitor : SyntaxWalkerBase
	{
		internal ParameterVisitor(IVisitorContext ctx) : base(ctx)
		{
		}

		public override void VisitIdentifierName(IdentifierNameSyntax node)
		{
			symbol = Context.SemanticModel.GetSymbolInfo(node).Symbol as ParameterSymbol;
		}

		public static ParameterSymbol Process(IVisitorContext context, ExpressionSyntax node)
		{
			var visitor = new ParameterVisitor(context);
			visitor.Visit(node);

			return visitor.symbol;
		}

		private ParameterSymbol symbol;
	}
}
