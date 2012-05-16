using System;
using System.Linq;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
	class CompilationUnitVisitor : SyntaxWalkerBase
	{
		internal CompilationUnitVisitor(IVisitorContext ctx) : base(ctx)
		{
		}

		protected override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
		{
			try
			{
				var namespaceHierarchy = node.AncestorsAndSelf().OfType<NamespaceDeclarationSyntax>().Reverse();
				var @namespace = namespaceHierarchy.Aggregate("",(acc, curr) => acc + "." + curr.Name.GetText());

				Context.Namespace = @namespace.StartsWith(".") ? @namespace.Substring(1) : @namespace;
				base.VisitNamespaceDeclaration(node);
			}
			finally
			{
				Context.Namespace = string.Empty;
			}
		}

		protected override void VisitClassDeclaration(ClassDeclarationSyntax node)
		{
			new TypeDeclarationVisitor(Context).Visit(node);
		}

		protected override void VisitStructDeclaration(StructDeclarationSyntax node)
		{
			new TypeDeclarationVisitor(Context).Visit(node);
		}

		protected override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
		{
			new TypeDeclarationVisitor(Context).Visit(node);
		}

		protected override void VisitEnumDeclaration(EnumDeclarationSyntax node)
		{
			throw new NotImplementedException();
		}
	}
}
