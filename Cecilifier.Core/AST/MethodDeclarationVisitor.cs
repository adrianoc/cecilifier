using System;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
	class MethodDeclarationVisitor : SyntaxWalkerBase
	{
		public MethodDeclarationVisitor(IVisitorContext context) : base(context)
		{
		}

		public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
		{
			LogUnsupportedSyntax(node);
		}
		
		public override void VisitBlock(BlockSyntax node)
	    {
		    StatementVisitor.Visit(Context, ilVar, node);
	    }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
		{
			ProcessMethodDeclaration(node, node.Identifier.ValueText, MethodNameOf(node), ResolveType(node.ReturnType), _ => base.VisitMethodDeclaration(node));
		}

		public override void VisitParameter(ParameterSyntax node)
		{
			var declaringMethodName = ".ctor";

			var declaringMethodOrCtor = (BaseMethodDeclarationSyntax) node.Parent.Parent;
			
			var declaringType = declaringMethodOrCtor.ResolveDeclaringType();

			if (node.Parent.Parent.IsKind(SyntaxKind.MethodDeclaration))
			{
				var declaringMethod = (MethodDeclarationSyntax) declaringMethodOrCtor;
				declaringMethodName = declaringMethod.Identifier.ValueText;
			}

			var paramVar = TempLocalVar(node.Identifier.ValueText);
			Context.DefinitionVariables.RegisterNonMethod(string.Empty, node.Identifier.ValueText, MemberKind.Parameter, paramVar);

			var tbf = new MethodDefinitionVariable(
				declaringType.Identifier.Text, 
				declaringMethodName, 
				declaringMethodOrCtor.ParameterList.Parameters.Select(p => Context.GetTypeInfo(p.Type).Type.Name).ToArray());
			
			var declaringMethodVariable = Context.DefinitionVariables.GetMethodVariable(tbf).VariableName;
			
			var exps = CecilDefinitionsFactory.Parameter(node, Context.SemanticModel, declaringMethodVariable, paramVar, ResolveType(node.Type));
			AddCecilExpressions(exps);

			base.VisitParameter(node);
		}

		public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
		{
			AddCecilExpression("[PropertyDeclaration] {0}", node);
			base.VisitPropertyDeclaration(node);
		}

	    protected void ProcessMethodDeclaration<T>(T node, string simpleName, string fqName, string returnType, Action<string> runWithCurrent) where T : BaseMethodDeclarationSyntax
		{
			var declaringTypeName = DeclaringTypeNameFor(node);

			var methodVar = MethodExtensions.LocalVariableNameFor(declaringTypeName, simpleName, node.MangleName(Context.SemanticModel));

			AddOrUpdateMethodDefinition(methodVar, fqName, node.Modifiers.MethodModifiersToCecil(ModifiersToCecil, GetSpecificModifiers(), DeclaredSymbolFor(node)), returnType);
			AddCecilExpression("{0}.Methods.Add({1});", Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName, methodVar);

			var isAbstract = DeclaredSymbolFor(node).IsAbstract;
			if (!isAbstract)
			{
				ilVar = MethodExtensions.LocalVariableNameFor("il", declaringTypeName, simpleName, node.MangleName(Context.SemanticModel));
				AddCecilExpression(@"var {0} = {1}.Body.GetILProcessor();", ilVar, methodVar);
			}

			WithCurrentMethod(declaringTypeName,  methodVar, fqName, node.ParameterList.Parameters.Select(p => Context.GetTypeInfo(p.Type).Type.Name).ToArray(), runWithCurrent);

			//TODO: Move this to default ctor handling and rely on VisitReturnStatement here instead
			if (!isAbstract && !node.DescendantNodes().Any(n => n.Kind() == SyntaxKind.ReturnStatement))
			{
				AddCilInstruction(ilVar, OpCodes.Ret);
			}
		}

		protected static string DeclaringTypeNameFor<T>(T node) where T : BaseMethodDeclarationSyntax
		{
			var declaringType = (TypeDeclarationSyntax) node.Parent;
			return declaringType.Identifier.ValueText;
		}

		private void AddOrUpdateMethodDefinition(string methodVar, string fqName, string methodModifiers, string returnType)
		{
			if (Context.Contains(methodVar))
			{
				AddCecilExpression("{0}.Attributes = {1};", methodVar, methodModifiers);
				AddCecilExpression("{0}.HasThis = !{0}.IsStatic;", methodVar);
			}
			else
			{
				AddMethodDefinition(Context, methodVar, fqName, methodModifiers, returnType);
			}
		}

		public static void AddMethodDefinition(IVisitorContext context, string methodVar, string fqName, string methodModifiers, string returnType)
		{
			context.WriteCecilExpression($"var {methodVar} = new MethodDefinition(\"{fqName}\", {methodModifiers}, {returnType});\r\n");
			context[methodVar] = "";
		}

		protected virtual string GetSpecificModifiers()
		{
			return null;
		}

		private string MethodNameOf(MethodDeclarationSyntax method)
		{
			return DeclaredSymbolFor(method).Name;
		}

	    protected string ilVar;
	}
}
