using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil;

namespace Cecilifier.Core.AST
{
	class TypeDeclarationVisitor : SyntaxWalkerBase
	{
		public TypeDeclarationVisitor(IVisitorContext context) : base(context)
		{
		}

		public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
		{
			HandleInterfaceDeclaration(node);
			base.VisitInterfaceDeclaration(node);
		}

		public override void VisitClassDeclaration(ClassDeclarationSyntax node)
		{
			HandleClassDeclaration(node, ProcessBase(node));
			base.VisitClassDeclaration(node);
		}

		public override void VisitStructDeclaration(StructDeclarationSyntax node)
		{
			HandleTypeDeclaration(node, ProcessBase(node));
			base.VisitStructDeclaration(node);
		}

		public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
		{
			new FieldDeclarationVisitor(Context).Visit(node);
		}

		public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
		{
			new ConstructorDeclarationVisitor(Context).Visit(node);
		}

		public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
		{
			new MethodDeclarationVisitor(Context).Visit(node);
		}
		
		private void SetDeclaringType(TypeDeclarationSyntax classDeclaration, string childLocalVar)
		{
			if (IsNestedTypeDeclaration(classDeclaration))
			{
				AddCecilExpression("{0}.NestedTypes.Add({1});", ResolveTypeLocalVariable(classDeclaration.Parent.ResolveDeclaringType()), childLocalVar);
			}
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

		private void HandleInterfaceDeclaration(TypeDeclarationSyntax node)
		{
			HandleTypeDeclaration(node, string.Empty);	
		}

		private void HandleClassDeclaration(TypeDeclarationSyntax node, string baseType)
		{
			HandleTypeDeclaration(node, baseType);	
		}

		private void HandleTypeDeclaration(TypeDeclarationSyntax node, string baseType)
		{
			var varName = LocalVariableNameForId(NextLocalVariableTypeId());

			AddCecilExpression("TypeDefinition {0} = new TypeDefinition(\"{1}\", \"{2}\", {3}{4});", varName, Context.Namespace, node.Identifier.Value, TypeModifiersToCecil(node), !string.IsNullOrWhiteSpace(baseType) ? ", " + baseType : "");
			
			foreach (var itfName in ImplementedInterfacesFor(node.BaseList))
			{
				AddCecilExpression("{0}.Interfaces.Add(new InterfaceImplementation({1}));", varName, ResolveType(itfName));
			}

			//TODO: Looks like a bug in Mono.Cecil
			if (!IsNestedTypeDeclaration(node))
			{
				AddCecilExpression("assembly.MainModule.Types.Add({0});", varName);
			}

			SetDeclaringType(node, varName);
			RegisterTypeLocalVariable(node, varName);

			EnsureCurrentTypeHasADefaultCtor(node, varName);
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

			if (!defaultCtorFound)
			{
				new ConstructorDeclarationVisitor(context).DefaultCtorInjector(localVarName, node);
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
			if (ctorNode.ParameterList.Parameters.Count > 0) return;

			defaultCtorFound = true;
		}

		private bool defaultCtorFound;
		private string localVarName;
	}
}
