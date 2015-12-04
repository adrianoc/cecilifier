using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Misc
{
	class LocalVariable
	{
		public LocalVariable(SyntaxNode node, string localVariable)
		{
			VarName = localVariable;
			SyntaxNode = node;
		}

		public string VarName { get; set; }
		private SyntaxNode SyntaxNode { get; set; }
	}
}
