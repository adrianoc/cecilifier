using System;
using System.Linq;
using Cecilifier.Core.Extensions;
using Mono.Cecil.Cil;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
	class ConstructorDeclarationVisitor : MethodDeclarationVisitor
	{

		public ConstructorDeclarationVisitor(IVisitorContext context) : base(context)
		{
		}

		protected override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
		{
			var declaringType = node.Parent.ResolveDeclaringType();
			Context.SetDefaultCtorInjectorFor(declaringType, delegate { });

            Action<ConstructorDeclarationSyntax> callBaseMethod = base.VisitConstructorDeclaration;
 
			var returnType = GetSpecialType(SpecialType.System_Void);
			ProcessMethodDeclaration(node, "ctor", ".ctor", ResolvePredefinedType(returnType), simpleName =>
			{
				AddCilInstruction(ilVar, OpCodes.Ldarg_0);
				callBaseMethod(node);

				if (!ctorAdded)
				{
					var declaringTypelocalVar = ResolveTypeLocalVariable(declaringType.Identifier.ValueText);

					AddCilInstruction(ilVar, OpCodes.Call, string.Format("assembly.MainModule.Import(DefaultCtorFor({0}.BaseType.Resolve()))));", declaringTypelocalVar));
				}
			});
		}

		protected override void VisitConstructorInitializer(ConstructorInitializerSyntax node)
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
			var ctorLocalVar = TempLocalVar("ctor");
			AddCecilExpression(@"var {0} = new MethodDefinition("".ctor"", {1} | {2} | MethodAttributes.HideBySig, assembly.MainModule.TypeSystem.Void);", ctorLocalVar, CtorFlags, DefaultCtorAccessibilityFor(declaringClass));
			AddCecilExpression(@"{0}.Methods.Add({1});", localVar, ctorLocalVar);
			var ilVar = TempLocalVar("il");
            AddCecilExpression(@"var {0} = {1}.Body.GetILProcessor();", ilVar, ctorLocalVar);

            AddCilInstruction(ilVar, OpCodes.Ldarg_0);
            AddCilInstruction(ilVar, OpCodes.Call, string.Format("assembly.MainModule.Import(DefaultCtorFor({0}.BaseType.Resolve()))", localVar));
            AddCilInstruction(ilVar, OpCodes.Ret);
		}

		private static string DefaultCtorAccessibilityFor(BaseTypeDeclarationSyntax declaringClass)
		{
			return declaringClass.Modifiers.Any(m => m.Kind == SyntaxKind.AbstractKeyword)
								? "MethodAttributes.Family"
								: "MethodAttributes.Public";
		}


		private bool ctorAdded;
		private const string CtorFlags = "MethodAttributes.RTSpecialName | MethodAttributes.SpecialName";
	}
}
