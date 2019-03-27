using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.Misc
{
	class TypeDeclarationResolver : CSharpSyntaxWalker
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

		public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
		{
			declaringType = node;
		}

		public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
		{
			declaringType = (BaseTypeDeclarationSyntax) node.Ancestors().Where(a => a.Kind() == SyntaxKind.ClassDeclaration || a.Kind() == SyntaxKind.StructDeclaration).First();
		}

		public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
		{
			Visit(node.Parent);
		}

		public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
		{
			Visit(node.Parent);
		}

		public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
		{
			Visit(node.Parent);
		}

		public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
		{
			Visit(node.Parent);
		}
	}
}
