using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
	class MethodDeclarationVisitor : SyntaxWalkerBase
	{
		public MethodDeclarationVisitor(IVisitorContext context) : base(context)
		{
		}

		protected override void VisitMethodDeclaration(MethodDeclarationSyntax node)
		{
			ProcessMethodDeclaration(node, node.Identifier.ValueText, MethodNameOf(node), ResolveType(node.ReturnType), _ => base.VisitMethodDeclaration(node));
		}

		protected override void VisitParameter(ParameterSyntax node)
		{
			var methodVar = LocalVariableNameForCurrentNode();
			AddCecilExpression("{0}.Parameters.Add(new ParameterDefinition(\"{1}\", ParameterAttributes.None, {2}));", methodVar, node.Identifier.ValueText, ResolveType(node.TypeOpt));
			base.VisitParameter(node);
		}

		protected override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
		{
			AddCecilExpression("[PropertyDeclaration] {0}", node);
			base.VisitPropertyDeclaration(node);
		}

		protected void ProcessMethodDeclaration<T>(T node, string simpleName, string fqName, string returnType, Action<string> runWithCurrent) where T : BaseMethodDeclarationSyntax
		{
			var declaringType = (TypeDeclarationSyntax)node.Parent;
			var declaringTypeName = declaringType.Identifier.ValueText;

			var methodVar = LocalVariableNameFor("method", declaringTypeName, simpleName);
			AddCecilExpression("var {0} = new MethodDefinition(\"{1}\", {2}, {3});", methodVar, fqName, MethodModifiersToCecil(node), returnType);
			AddCecilExpression("{0}.Methods.Add({1});", ResolveLocalVariable(declaringTypeName), methodVar);

			var isAbstract = DeclaredSymbolFor(node).IsAbstract;
			string ilVar = null;
			if (!isAbstract)
			{
				ilVar = LocalVariableNameFor("il", declaringTypeName, simpleName);
				AddCecilExpression(@"var {0} = {1}.Body.GetILProcessor();", ilVar, methodVar);
			}

			WithCurrentNode(node, methodVar, simpleName, runWithCurrent);

			if (!isAbstract) AddCecilExpression(@"{0}.Body.Instructions.Add({1}.Create(OpCodes.Ret));", methodVar, ilVar);
		}

		private string MethodModifiersToCecil(BaseMethodDeclarationSyntax methodDeclaration)
		{
			var modifiers = MapExplicityModifiers(methodDeclaration);

			var defaultAccessibility = "MethodAttributes.Private";
			if (modifiers == string.Empty)
			{
				var methodSymbol = DeclaredSymbolFor(methodDeclaration);
				if (IsExplicityMethodImplementation(methodSymbol))
				{
					modifiers = "MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.Final";
				}
				else
				{
					var lastDeclaredIn = methodSymbol.FindLastDefinition();
					if (lastDeclaredIn.ContainingType.TypeKind == TypeKind.Interface)
					{
						modifiers = "MethodAttributes.Virtual | MethodAttributes.NewSlot | " + (lastDeclaredIn.ContainingType == methodSymbol.ContainingType ? "MethodAttributes.Abstract" : "MethodAttributes.Final");
						defaultAccessibility = lastDeclaredIn.ContainingType == methodSymbol.ContainingType ? "MethodAttributes.Public" : "MethodAttributes.Private";
					}
				}
			}

			var validModifiers = RemoveSourceModifiersWithNoILEquivalent(methodDeclaration);

			var cecilModifiersStr = ModifiersToCecil("MethodAttributes", validModifiers.ToList(), defaultAccessibility);

			cecilModifiersStr = AppendSpecificModifiers(cecilModifiersStr);

			return cecilModifiersStr + " | MethodAttributes.HideBySig".AppendModifier(modifiers);
		}

		protected virtual string AppendSpecificModifiers(string cecilModifiersStr)
		{
			return cecilModifiersStr;
		}

		private static string MapExplicityModifiers(BaseMethodDeclarationSyntax methodDeclaration)
		{
			foreach (var mod in methodDeclaration.Modifiers)
			{
				switch (mod.Kind)
				{
					case SyntaxKind.VirtualKeyword:  return "MethodAttributes.Virtual | MethodAttributes.NewSlot";
					case SyntaxKind.OverrideKeyword: return "MethodAttributes.Virtual";
					case SyntaxKind.AbstractKeyword: return "MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.Abstract";
					case SyntaxKind.SealedKeyword:   return "MethodAttributes.Final";
					case SyntaxKind.NewKeyword:      return "??? new ??? dont know yet!";
				}
			}
			return string.Empty;
		}

		private static bool IsExplicityMethodImplementation(MethodSymbol methodSymbol)
		{
			return methodSymbol.ExplicitInterfaceImplementations.Count > 0;
		}

		private static IEnumerable<SyntaxToken> RemoveSourceModifiersWithNoILEquivalent(BaseMethodDeclarationSyntax methodDeclaration)
		{
			return methodDeclaration.Modifiers.Where(
				mod => (mod.Kind != SyntaxKind.OverrideKeyword 
				        && mod.Kind != SyntaxKind.AbstractKeyword 
				        && mod.Kind != SyntaxKind.VirtualKeyword 
				        && mod.Kind != SyntaxKind.SealedKeyword));
		}

		private string MethodNameOf(MethodDeclarationSyntax method)
		{
			return DeclaredSymbolFor(method).Name;
		}
	}
}
