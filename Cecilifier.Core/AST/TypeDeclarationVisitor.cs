using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
	class TypeDeclarationVisitor : TypeDeclarationVisitorBase
	{
		public TypeDeclarationVisitor(IVisitorContext context) : base(context)
		{
		}

		public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
		{
			var definitionVar =  HandleInterfaceDeclaration(node);
			using(Context.DefinitionVariables.WithCurrent("", node.Identifier.ValueText, MemberKind.Type, definitionVar))
			{
				base.VisitInterfaceDeclaration(node);
			}
		}

		public override void VisitClassDeclaration(ClassDeclarationSyntax node)
		{
			var definitionVar = HandleClassDeclaration(node, ProcessBase(node));
			using(Context.DefinitionVariables.WithCurrent("", node.Identifier.ValueText, MemberKind.Type, definitionVar))
			{
				base.VisitClassDeclaration(node);
			}
		}

		public override void VisitStructDeclaration(StructDeclarationSyntax node)
		{
			var definitionVar = HandleTypeDeclaration(node, ProcessBase(node));
			using(Context.DefinitionVariables.WithCurrent("", node.Identifier.ValueText, MemberKind.Type, definitionVar))
			{
				base.VisitStructDeclaration(node);
			}
		}

		public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
		{
			new FieldDeclarationVisitor(Context).Visit(node);
		}

		public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
		{
			new PropertyDeclarationVisitor(Context).Visit(node);
		}

		public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
		{
			new PropertyDeclarationVisitor(Context).Visit(node);
		}

		public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
		{
			new ConstructorDeclarationVisitor(Context).Visit(node);
		}

		public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
		{
			new MethodDeclarationVisitor(Context).Visit(node);
		}
		
		private string ProcessBase(TypeDeclarationSyntax classDeclaration)
		{
			var classSymbol = DeclaredSymbolFor(classDeclaration);
			var baseTypeName = classSymbol.BaseType.Name;
			
			return ResolveTypeLocalVariable(baseTypeName) ?? ResolveType(baseTypeName);
		}

		private IEnumerable<string> ImplementedInterfacesFor(BaseListSyntax bases)
		{
			if (bases == null) yield break;

			foreach (var @base in bases.Types)
			{
				var info = Context.GetTypeInfo(@base.Type);
				if (info.Type.TypeKind == TypeKind.Interface)
				{
					var itfFQName = @base.DescendantTokens().OfType<SyntaxToken>().Aggregate("", (acc, curr) => acc + curr.ValueText);
					yield return itfFQName;
				}
			}
		}

		private string HandleInterfaceDeclaration(TypeDeclarationSyntax node)
		{
			return HandleTypeDeclaration(node, string.Empty);	
		}

		private string HandleClassDeclaration(TypeDeclarationSyntax node, string baseType)
		{
			return HandleTypeDeclaration(node, baseType);	
		}

		private string HandleTypeDeclaration(TypeDeclarationSyntax node, string baseType)
		{
			var varName = LocalVariableNameForId(NextLocalVariableTypeId());
			bool isStructWithNoFields = node.Kind() == SyntaxKind.StructDeclaration && node.Members.Count == 0;
			AddCecilExpressions(CecilDefinitionsFactory.Type(Context, varName, node.Identifier.ValueText, TypeModifiersToCecil(node), baseType, isStructWithNoFields, ImplementedInterfacesFor(node.BaseList).Select(i => ResolveType(i))));

			EnsureCurrentTypeHasADefaultCtor(node, varName);
			
			HandleAttributesInTypeDeclaration(node, varName);

			return varName;
		}

		private void EnsureCurrentTypeHasADefaultCtor(TypeDeclarationSyntax node, string typeLocalVar)
		{
			node.Accept(new DefaultCtorVisitor(Context, typeLocalVar));
		}
	}

	internal class DefaultCtorVisitor : CSharpSyntaxWalker
	{
		private readonly IVisitorContext context;

		public DefaultCtorVisitor(IVisitorContext context, string localVarName)
		{
			this.localVarName = localVarName;
			this.context = context;
		}

		public override void VisitStructDeclaration(StructDeclarationSyntax node)
		{
			foreach (var member in NonTypeMembersOf(node))
			{
				member.Accept(this);
			}
		}

		public override void VisitClassDeclaration(ClassDeclarationSyntax node)
		{
			foreach (var member in NonTypeMembersOf(node))
			{
				member.Accept(this);
			}
			
			if (!defaultCtorFound)
			{
				new ConstructorDeclarationVisitor(context).DefaultCtorInjector(localVarName, node);
			}
		}

		private static IEnumerable<MemberDeclarationSyntax> NonTypeMembersOf(TypeDeclarationSyntax node)
		{
			return node.Members.Where(m => m.Kind() != SyntaxKind.ClassDeclaration && m.Kind() != SyntaxKind.StructDeclaration && m.Kind() != SyntaxKind.EnumDeclaration);
		}

		public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax ctorNode)
		{
            // bailout in case the ctor have parameters or is static (a cctor)
			if (ctorNode.ParameterList.Parameters.Count > 0 || ctorNode.Modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword)))
			    return;

			defaultCtorFound = true;
		}

		private bool defaultCtorFound;
		private string localVarName;
	}
}
