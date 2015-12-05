using System;
using System.Linq;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
	class ConstructorDeclarationVisitor : MethodDeclarationVisitor
	{
		public ConstructorDeclarationVisitor(IVisitorContext context) : base(context)
		{
		}

		public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
		{
			// Why return for parameterless ctors ???
			//if (node.ParameterList.Parameters.Count == 0) return;

			var declaringType = node.Parent.ResolveDeclaringType();

            Action<ConstructorDeclarationSyntax> callBaseMethod = base.VisitConstructorDeclaration;
 
			var returnType = GetSpecialType(SpecialType.System_Void);
			ProcessMethodDeclaration(node, "ctor", ".ctor", ResolvePredefinedType(returnType), simpleName =>
			{
				if (declaringType.Kind() != SyntaxKind.StructDeclaration)
				{
					AddCilInstruction(ilVar, OpCodes.Ldarg_0);
				}

				using (new MethodParametersContext(Context))
				{
					callBaseMethod(node);
				}

				if (!ctorAdded && declaringType.Kind() != SyntaxKind.StructDeclaration)
				{
					var declaringTypelocalVar = ResolveTypeLocalVariable(declaringType.Identifier.ValueText);
					AddCilInstruction(ilVar, OpCodes.Call, string.Format("assembly.MainModule.Import(TypeHelpers.DefaultCtorFor({0}.BaseType.Resolve()))", declaringTypelocalVar));
				}
			});
		}

		public override void VisitConstructorInitializer(ConstructorInitializerSyntax node)
        {
            ctorAdded = true;
			new ConstructorInitializerVisitor(Context, ilVar).Visit(node);
        }
		
		protected override string AppendSpecificModifiers(string cecilModifiersStr)
		{
			return cecilModifiersStr.AppendModifier(CtorFlags);
		}

		internal void DefaultCtorInjector(string localVar, BaseTypeDeclarationSyntax declaringClass)
		{
			var ctorLocalVar = MethodExtensions.LocalVariableNameFor(declaringClass.Identifier.ValueText, new[] {"ctor", ""});

			AddCecilExpression(@"var {0} = new MethodDefinition("".ctor"", {1} | {2} | MethodAttributes.HideBySig, assembly.MainModule.TypeSystem.Void);", ctorLocalVar, CtorFlags, DefaultCtorAccessibilityFor(declaringClass));
			AddCecilExpression(@"{0}.Methods.Add({1});", localVar, ctorLocalVar);
			var ilVar = TempLocalVar("il");
			AddCecilExpression(@"var {0} = {1}.Body.GetILProcessor();", ilVar, ctorLocalVar);

			AddCilInstruction(ilVar, OpCodes.Ldarg_0);
			AddCilInstruction(ilVar, OpCodes.Call, string.Format("assembly.MainModule.Import(TypeHelpers.DefaultCtorFor({0}.BaseType.Resolve()))", localVar));
			AddCilInstruction(ilVar, OpCodes.Ret);

			Context[ctorLocalVar] = "";
		}

		private static string DefaultCtorAccessibilityFor(BaseTypeDeclarationSyntax declaringClass)
		{
			return declaringClass.Modifiers.Any(m => m.Kind() == SyntaxKind.AbstractKeyword)
								? "MethodAttributes.Family"
								: "MethodAttributes.Public";
		}


		private bool ctorAdded;
		private const string CtorFlags = "MethodAttributes.RTSpecialName | MethodAttributes.SpecialName";
	}
}
