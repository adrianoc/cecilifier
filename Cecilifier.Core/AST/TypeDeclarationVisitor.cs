using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
	class TypeDeclarationVisitor : SyntaxWalkerBase
	{
		public TypeDeclarationVisitor(IVisitorContext context) : base(context)
		{
		}

		protected override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
		{
			HandleInterfaceDeclaration(node);
			base.VisitInterfaceDeclaration(node);
		}

		protected override void VisitClassDeclaration(ClassDeclarationSyntax node)
		{
			HandleClassDeclaration(node, ProcessBase(node));
			base.VisitClassDeclaration(node);
		}

		protected override void VisitFieldDeclaration(FieldDeclarationSyntax node)
		{
			new FieldDeclarationVisitor(Context).Visit(node);
		}

		protected override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
		{
			new ConstructorDeclarationVisitor(Context).Visit(node);
		}

		protected override void VisitMethodDeclaration(MethodDeclarationSyntax node)
		{
			new MethodDeclarationVisitor(Context).Visit(node);
		}
		
		private void SetDeclaringType(TypeDeclarationSyntax classDeclaration, string localVariable)
		{
			if (classDeclaration.Parent.Kind == SyntaxKind.ClassDeclaration)
			{
				AddCecilExpression("{0}.DeclaringType = {1};", localVariable, ResolveLocalVariable(classDeclaration.Parent.ResolveDeclaringType()));
			}
		}

		private string ProcessBase(ClassDeclarationSyntax classDeclaration)
		{
			var classSymbol = DeclaredSymbolFor(classDeclaration);
			var baseTypeName = classSymbol.BaseType.Name;
			
			return ResolveLocalVariable(baseTypeName) ?? ResolveType(baseTypeName);
		}

		private IEnumerable<string> ImplementedInterfacesFor(BaseListSyntax bases)
		{
			if (bases == null) yield break;

			foreach (var @base in bases.Types)
			{
				var info = SemanticInfoFor(@base);
				if (info.Type.TypeKind == TypeKind.Interface)
				{
					var itfFQName = @base.DescendentTokens().OfType<SyntaxToken>().Aggregate("", (acc, curr) => acc + curr.ValueText);
					yield return itfFQName;
				}
			}
		}

		private void EnsureCurrentTypeHasADefaultCtor()
		{
			Context.EnsureCtorDefinedForCurrentType();
		}

		private void HandleInterfaceDeclaration(TypeDeclarationSyntax node)
		{
			HandleTypeDeclaration(node, string.Empty, delegate {});	
		}

		private void HandleClassDeclaration(TypeDeclarationSyntax node, string baseType)
		{
			HandleTypeDeclaration(node, baseType, new ConstructorDeclarationVisitor(Context).DefaultCtorInjector);	
		}

		private void HandleTypeDeclaration(TypeDeclarationSyntax node, string baseType, Action<string, BaseTypeDeclarationSyntax> ctorInjector)
		{
			EnsureCurrentTypeHasADefaultCtor();

			var varName = LocalVariableNameForId(NextLocalVariableTypeId());

			AddCecilExpression("TypeDefinition {0} = new TypeDefinition(\"{1}\", \"{2}\", {3}{4});", varName, Context.Namespace, node.Identifier.Value, TypeModifiersToCecil(node), !string.IsNullOrWhiteSpace(baseType) ? ", " + baseType : "");

			foreach (var itfName in ImplementedInterfacesFor(node.BaseListOpt))
			{
				AddCecilExpression("{0}.Interfaces.Add({1});", varName, ResolveType(itfName));
			}

			AddCecilExpression("assembly.MainModule.Types.Add({0});", varName);

			SetDeclaringType(node, varName);
			RegisterTypeLocalVariable(node, varName, ctorInjector);
		}
	}
}
