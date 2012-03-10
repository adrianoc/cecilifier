using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.Misc
{
	class TypeDeclarationResolver : SyntaxWalker
	{
		private BaseTypeDeclarationSyntax declaringType;

		public BaseTypeDeclarationSyntax Resolve(SyntaxNode node)
		{
			Visit(node);
			return declaringType;
		}

		protected override void VisitClassDeclaration(ClassDeclarationSyntax node)
		{
			declaringType = node;
		}

		protected override void VisitEnumDeclaration(EnumDeclarationSyntax node)
		{
			declaringType = node;
		}
	}
}
