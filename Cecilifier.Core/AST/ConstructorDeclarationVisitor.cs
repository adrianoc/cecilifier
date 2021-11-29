using System;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
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
            var variableName = Context.Naming.Constructor(node.ResolveDeclaringType<BaseTypeDeclarationSyntax>(), true);
            ProcessMethodDeclaration(node, variableName, "cctor", ".cctor", false, ctorVar => { node.Body.Accept(this); });
        }

        private void HandleInstanceConstructor(ConstructorDeclarationSyntax node)
        {
            var declaringType = node.Parent.ResolveDeclaringType<TypeDeclarationSyntax>();

            Action<ConstructorDeclarationSyntax> callBaseMethod = base.VisitConstructorDeclaration;

            var ctorVariable = Context.Naming.Constructor(declaringType, false);
            ProcessMethodDeclaration(node, ctorVariable, "ctor", ".ctor", false, ctorVar =>
            {
                if (node.Initializer == null || node.Initializer.IsKind(SyntaxKind.BaseConstructorInitializer)) 
                    ProcessFieldInitialization(declaringType, ilVar);

                if (declaringType.Kind() != SyntaxKind.StructDeclaration)
                {
                    AddCilInstruction(ilVar, OpCodes.Ldarg_0);
                }
               
                // If this ctor has an initializer the call to the base ctor will happen when we visit call base.VisitConstructorDeclaration()
                // otherwise we need to call it here.
                if (node.Initializer == null && declaringType.Kind() != SyntaxKind.StructDeclaration)
                {
                    var declaringTypeLocalVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Type).VariableName;
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

        internal void DefaultCtorInjector(string typeDefVar, ClassDeclarationSyntax declaringClass)
        {
            Context.WriteNewLine();
            Context.WriteComment($"** Constructor: {declaringClass.Identifier}() **");
            
            var ctorLocalVar = AddOrUpdateParameterlessCtorDefinition(
                                    declaringClass.Identifier.Text,
                                    DefaultCtorAccessibilityFor(declaringClass),
                                    Context.Naming.Constructor(declaringClass, false));
                                    
            AddCecilExpression($"{typeDefVar}.Methods.Add({ctorLocalVar});");

            var ctorBodyIL = Context.Naming.ILProcessor("ctor", declaringClass.Identifier.Text);
            AddCecilExpression($@"var {ctorBodyIL} = {ctorLocalVar}.Body.GetILProcessor();");
            
            ProcessFieldInitialization(declaringClass, ctorBodyIL);
            AddCilInstruction(ctorBodyIL, OpCodes.Ldarg_0);
            AddCilInstruction(ctorBodyIL, OpCodes.Call, ResolveDefaultCtorFor(typeDefVar, declaringClass));
            
            AddCilInstruction(ctorBodyIL, OpCodes.Ret);
        }
        
        internal void DefaultCtorInjector2(string typeDefVar, string typeName)
        {
            Context.WriteNewLine();
            Context.WriteComment($"** Constructor: {typeName}() **");
            var ctorLocalVar = AddOrUpdateParameterlessCtorDefinition(
                                            typeName,
                                            "MethodAttributes.Public",
                                            Context.Naming.SyntheticVariable(typeName, ElementKind.StaticConstructor));

            AddCecilExpression($"{typeDefVar}.Methods.Add({ctorLocalVar});");

            var ctorBodyIL = Context.Naming.ILProcessor("ctor", typeName);
            AddCecilExpression($@"var {ctorBodyIL} = {ctorLocalVar}.Body.GetILProcessor();");
            
            AddCilInstruction(ctorBodyIL, OpCodes.Ldarg_0);
            AddCilInstruction(ctorBodyIL, OpCodes.Call, Utils.ImportFromMainModule($"TypeHelpers.DefaultCtorFor({typeDefVar}.BaseType)"));
            AddCilInstruction(ctorBodyIL, OpCodes.Ret);
        }

        private string AddOrUpdateParameterlessCtorDefinition(string typeName, string ctorAccessibility, string ctorLocalVar)
        {
            var found = Context.DefinitionVariables.GetMethodVariable(new MethodDefinitionVariable(typeName, ".ctor", Array.Empty<string>()));
            if (found.IsValid)
            {
                ctorLocalVar = found.VariableName;
                
                AddCecilExpression($"{ctorLocalVar}.Attributes = {ctorAccessibility} | MethodAttributes.HideBySig | {CtorFlags};");
                AddCecilExpression($"{ctorLocalVar}.HasThis = !{ctorLocalVar}.IsStatic;", ctorLocalVar);
                return ctorLocalVar;
            }

            var ctorMethodDefinitionExp = CecilDefinitionsFactory.Constructor(Context, ctorLocalVar, typeName, ctorAccessibility, Array.Empty<string>());
            AddCecilExpression(ctorMethodDefinitionExp);
            return ctorLocalVar;
        }

        private void ProcessFieldInitialization(TypeDeclarationSyntax declaringClass, string ctorBodyIL)
        {
            foreach (var fieldDeclaration in declaringClass.Members.OfType<FieldDeclarationSyntax>().Where(f => !f.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword))))
            {
                var dec = fieldDeclaration.Declaration.Variables[0];
                if (dec.Initializer != null && !fieldDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
                {
                    Context.WriteNewLine();
                    Context.WriteComment(fieldDeclaration.ToString());
                    AddCilInstruction(ctorBodyIL, OpCodes.Ldarg_0);
                }

                if (ExpressionVisitor.Visit(Context, ctorBodyIL, dec.Initializer))
                    continue;

                var fieldVarDef = Context.DefinitionVariables.GetVariable(dec.Identifier.ValueText, VariableMemberKind.Field, declaringClass.Identifier.Text);
                var fieldStoreOpCode = fieldDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))
                    ? OpCodes.Stsfld
                    : OpCodes.Stfld;

                AddCilInstruction(ctorBodyIL, fieldStoreOpCode, fieldVarDef.VariableName);
            }
        }

        private string ResolveDefaultCtorFor(string typeDefVar, BaseTypeDeclarationSyntax type)
        {
            var typeSymbol = Context.GetDeclaredSymbol(type);
            if (typeSymbol == null)
                return Utils.ImportFromMainModule($"TypeHelpers.DefaultCtorFor({typeDefVar}.BaseType)");
            
            var baseTypeVarDef = Context.TypeResolver.ResolveLocalVariableType(typeSymbol.BaseType);
            if (baseTypeVarDef != null)
            {
                return $"new MethodReference(\".ctor\", {Context.TypeResolver.Bcl.System.Void} ,{baseTypeVarDef}) {{ HasThis = true }}";
            }

            return Utils.ImportFromMainModule($"TypeHelpers.DefaultCtorFor({typeDefVar}.BaseType)");
        }

        private static string DefaultCtorAccessibilityFor(BaseTypeDeclarationSyntax declaringClass)
        {
            return declaringClass.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword))
                ? "MethodAttributes.Family"
                : "MethodAttributes.Public";
        }
    }
}
