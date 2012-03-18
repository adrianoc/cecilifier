using System;
using System.Linq;
using Cecilifier.Core.Extensions;
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
				var ctorLocalVar = LocalVariableNameForCurrentNode();
				var declaringTypelocalVar = ResolveLocalVariable(declaringType.Identifier.ValueText);
				//TODO: Replace with AddCilInstruction
                AddCecilExpression(@"{0}.Body.Instructions.Add({1}.Create(OpCodes.Ldarg_0));", ctorLocalVar, ilVar);
				AddCecilExpression(@"{0}.Body.Instructions.Add({1}.Create(OpCodes.Call, assembly.MainModule.Import(DefaultCtorFor({2}.BaseType.Resolve()))));", ctorLocalVar, ilVar, declaringTypelocalVar);

				callBaseMethod(node);
			});
		}

        protected override void VisitConstructorInitializer(ConstructorInitializerSyntax node)
        {
            try
            {
                Context["il"] = ilVar;
                new ConstructorInitializerVisitor(Context).Visit(node);
            }
            finally
            {
                Context.Remove("il");
            }
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
			//TODO: Replace with AddCilInstruction
            AddCecilExpression(@"var {0} = {1}.Body.GetILProcessor();", ilVar, ctorLocalVar);

            //TODO: Replace with AddCilInstruction
            AddCecilExpression(@"{0}.Body.Instructions.Add({1}.Create(OpCodes.Ldarg_0));", ctorLocalVar, ilVar);
			AddCecilExpression(@"{0}.Body.Instructions.Add({1}.Create(OpCodes.Call, assembly.MainModule.Import(DefaultCtorFor({2}.BaseType.Resolve()))));", ctorLocalVar, ilVar, localVar);
			AddCecilExpression(@"{0}.Body.Instructions.Add({1}.Create(OpCodes.Ret));", ctorLocalVar, ilVar);
		}

		private static string DefaultCtorAccessibilityFor(BaseTypeDeclarationSyntax declaringClass)
		{
			return declaringClass.Modifiers.Any(m => m.Kind == SyntaxKind.AbstractKeyword)
								? "MethodAttributes.Family"
								: "MethodAttributes.Public";
		}


		private const string CtorFlags = "MethodAttributes.RTSpecialName | MethodAttributes.SpecialName";
	}
}
