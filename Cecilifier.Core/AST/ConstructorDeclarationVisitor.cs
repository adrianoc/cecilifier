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
    internal class ConstructorDeclarationVisitor : MethodDeclarationVisitor
    {
        public const string CtorFlags = "MethodAttributes.RTSpecialName | MethodAttributes.SpecialName";

        public ConstructorDeclarationVisitor(IVisitorContext context) : base(context)
        {
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            if (node.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
            {
                HandleStaticConstructor(node);
            }
            else
            {
                HandleInstanceConstructor(node);
            }
        }

        private void HandleStaticConstructor(ConstructorDeclarationSyntax node)
        {
            var returnType = GetSpecialType(SpecialType.System_Void);
            ProcessMethodDeclaration(node, "cctor", ".cctor", Context.TypeResolver.ResolvePredefinedType(returnType), ctorVar => { node.Body.Accept(this); });
        }

        private void HandleInstanceConstructor(ConstructorDeclarationSyntax node)
        {
            var declaringType = node.Parent.ResolveDeclaringType();

            Action<ConstructorDeclarationSyntax> callBaseMethod = base.VisitConstructorDeclaration;

            var returnType = GetSpecialType(SpecialType.System_Void);
            ProcessMethodDeclaration(node, "ctor", ".ctor", Context.TypeResolver.ResolvePredefinedType(returnType), ctorVar =>
            {
                if (declaringType.Kind() != SyntaxKind.StructDeclaration)
                {
                    AddCilInstruction(ilVar, OpCodes.Ldarg_0);
                }

                // If this ctor has an initializer the call to the base ctor will happen when we visit call base.VisitConstructorDeclaration()
                // otherwise we need to call it here.
                if (node.Initializer == null && declaringType.Kind() != SyntaxKind.StructDeclaration)
                {
                    var declaringTypeLocalVar = Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName;
                    AddCilInstruction(ilVar, OpCodes.Call, Utils.ImportFromMainModule($"TypeHelpers.DefaultCtorFor({declaringTypeLocalVar}.BaseType)"));
                }

                callBaseMethod(node);
            });
        }

        public override void VisitConstructorInitializer(ConstructorInitializerSyntax node)
        {
            new ConstructorInitializerVisitor(Context, ilVar).Visit(node);
        }

        protected override string GetSpecificModifiers()
        {
            return string.Empty.AppendModifier(CtorFlags);
        }

        internal void DefaultCtorInjector(string typeDefVar, BaseTypeDeclarationSyntax declaringClass)
        {
            var ctorMethodDefinitionExp = CecilDefinitionsFactory.Constructor(Context, out var ctorLocalVar, declaringClass.Identifier.ValueText, DefaultCtorAccessibilityFor(declaringClass), Array.Empty<string>());
            AddCecilExpression(ctorMethodDefinitionExp);
            AddCecilExpression($"{typeDefVar}.Methods.Add({ctorLocalVar});");

            var ctorBodyIL = TempLocalVar("il");

            AddCecilExpression($@"var {ctorBodyIL} = {ctorLocalVar}.Body.GetILProcessor();");

            AddCilInstruction(ctorBodyIL, OpCodes.Ldarg_0);
            AddCilInstruction(ctorBodyIL, OpCodes.Call, ResolveDefaultCtorFor(typeDefVar, declaringClass));
            AddCilInstruction(ctorBodyIL, OpCodes.Ret);

            Context[ctorLocalVar] = "";
        }

        private string ResolveDefaultCtorFor(string typeDefVar, BaseTypeDeclarationSyntax type)
        {
            var typeSymbol = Context.GetDeclaredSymbol(type);
            if (typeSymbol == null)
            {
            }

            var baseTypeVarDef = Context.TypeResolver.ResolveTypeLocalVariable(typeSymbol.BaseType.Name);
            var ts = (INamedTypeSymbol) typeSymbol;
            if (ts.BaseType.IsGenericType && baseTypeVarDef != null)
            {
                var genericTypeExp = $"{ts.BaseType.TypeArguments.Select(arg => Context.TypeResolver.Resolve(arg)).Aggregate((acc, curr) => acc + "," + curr)}";
                return $"new MethodReference(\".ctor\", {Context.TypeResolver.ResolvePredefinedType("Void")} ,{baseTypeVarDef}.MakeGenericInstanceType({genericTypeExp})) {{ HasThis = true }}";
            }
            
            // Non generic base type
            return Utils.ImportFromMainModule($"TypeHelpers.DefaultCtorFor({typeDefVar}.BaseType)");
        }

        private static string DefaultCtorAccessibilityFor(BaseTypeDeclarationSyntax declaringClass)
        {
            return declaringClass.Modifiers.Any(m => m.Kind() == SyntaxKind.AbstractKeyword)
                ? "MethodAttributes.Family"
                : "MethodAttributes.Public";
        }
    }
}
