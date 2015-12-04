using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
	class CompilationUnitVisitor : SyntaxWalkerBase
	{
		internal CompilationUnitVisitor(IVisitorContext ctx) : base(ctx)
		{
		}

		public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
		{
			try
			{
				var namespaceHierarchy = node.AncestorsAndSelf().OfType<NamespaceDeclarationSyntax>().Reverse();
				var @namespace = namespaceHierarchy.Aggregate("",(acc, curr) => acc + "." + curr.Name.WithoutTrivia().ToString());

				Context.Namespace = @namespace.StartsWith(".") ? @namespace.Substring(1) : @namespace;
				base.VisitNamespaceDeclaration(node);
			}
			finally
			{
				Context.Namespace = string.Empty;
			}
		}

		public override void VisitClassDeclaration(ClassDeclarationSyntax node)
		{
			new TypeDeclarationVisitor(Context).Visit(node);
		}

		public override void VisitStructDeclaration(StructDeclarationSyntax node)
		{
			new TypeDeclarationVisitor(Context).Visit(node);
		}

		public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
		{
			new TypeDeclarationVisitor(Context).Visit(node);
		}

		public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
		{
			throw new NotImplementedException();
		}
	}
}
