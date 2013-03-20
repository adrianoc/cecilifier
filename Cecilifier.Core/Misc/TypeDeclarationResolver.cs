using System.Linq;
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

		public override void VisitStructDeclaration(StructDeclarationSyntax node)
		{
			declaringType = node;
		}

		public override void VisitClassDeclaration(ClassDeclarationSyntax node)
		{
			declaringType = node;
		}

		public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
		{
			declaringType = node;
		}

		public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
		{
			declaringType =
				(BaseTypeDeclarationSyntax) node.Ancestors().Where(a => a.Kind == SyntaxKind.ClassDeclaration || a.Kind == SyntaxKind.StructDeclaration).Single();
		}
	}
}
