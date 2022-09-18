using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
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
            ProcessMethodDeclaration(node, variableName, Constants.Cecil.StaticConstructorName, $".{Constants.Cecil.StaticConstructorName}", false, ctorVar =>
            {
                var declaringType = node.Parent.ResolveDeclaringType<TypeDeclarationSyntax>();
                ProcessFieldInitialization(declaringType, ilVar, true);
                base.VisitConstructorDeclaration(node);                
            });
        }

        private void HandleInstanceConstructor(ConstructorDeclarationSyntax node)
        {
            var declaringType = node.Parent.ResolveDeclaringType<TypeDeclarationSyntax>();

            var callBaseMethod = base.VisitConstructorDeclaration;

            var ctorVariable = Context.Naming.Constructor(declaringType, false);
            ProcessMethodDeclaration(node, ctorVariable, "ctor", ".ctor", false, ctorVar =>
            {
                if (node.Initializer == null || node.Initializer.IsKind(SyntaxKind.BaseConstructorInitializer)) 
                    ProcessFieldInitialization(declaringType, ilVar, false);

                if (declaringType.Kind() != SyntaxKind.StructDeclaration)
                {
                    Context.EmitCilInstruction(ilVar, OpCodes.Ldarg_0);
                }
               
                // If this ctor has an initializer the call to the base ctor will happen when we visit call base.VisitConstructorDeclaration()
                // otherwise we need to call it here.
                if (node.Initializer == null && declaringType.Kind() != SyntaxKind.StructDeclaration)
                {
                    var declaringTypeLocalVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Type).VariableName;
                    var operand = Utils.ImportFromMainModule($"TypeHelpers.DefaultCtorFor({declaringTypeLocalVar}.BaseType)");
                    Context.EmitCilInstruction(ilVar, OpCodes.Call, operand);
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
            return string.Empty.AppendModifier(Constants.Cecil.CtorAttributes);
        }

        internal void DefaultCtorInjector(string typeDefVar, ClassDeclarationSyntax declaringClass, bool isStatic)
        {
            DefaultCtorInjector(typeDefVar, declaringClass.Identifier.Text, DefaultCtorAccessibilityFor(declaringClass, isStatic), ResolveDefaultCtorFor(typeDefVar, declaringClass), isStatic, ctorBodyIL =>
            {
                ProcessFieldInitialization(declaringClass, ctorBodyIL, isStatic);
            });
        }

        internal void DefaultCtorInjector(string typeDefVar, string typeName, string ctorAccessibility, string baseCtor, bool isStatic, Action<string> processInitializers)
        {
            Context.WriteNewLine();
            Context.WriteComment($"** Constructor: {typeName}() **");

            var ctorLocalVar = AddOrUpdateParameterlessCtorDefinition(
                typeName,
                ctorAccessibility,
                isStatic,
                Context.Naming.SyntheticVariable(typeName, isStatic ? ElementKind.StaticConstructor : ElementKind.Constructor));
                                    
            AddCecilExpression($"{typeDefVar}.Methods.Add({ctorLocalVar});");

            var ctorBodyIL = Context.Naming.ILProcessor($"ctor_{typeName}");
            AddCecilExpression($@"var {ctorBodyIL} = {ctorLocalVar}.Body.GetILProcessor();");
            
            processInitializers?.Invoke(ctorBodyIL);
            if (!isStatic)
            {
                Context.EmitCilInstruction(ctorBodyIL, OpCodes.Ldarg_0);
                Context.EmitCilInstruction(ctorBodyIL, OpCodes.Call, baseCtor);
            }
            
            Context.EmitCilInstruction(ctorBodyIL, OpCodes.Ret);
        }

        private string AddOrUpdateParameterlessCtorDefinition(string typeName, string ctorAccessibility, bool isStatic, string ctorLocalVar)
        {
            var found = Context.DefinitionVariables.GetMethodVariable(new MethodDefinitionVariable(typeName, Utils.ConstructorMethodName(isStatic), Array.Empty<string>()));
            if (found.IsValid)
            {
                ctorLocalVar = found.VariableName;
                
                AddCecilExpression($"{ctorLocalVar}.Attributes = {ctorAccessibility} | MethodAttributes.HideBySig | {Constants.Cecil.CtorAttributes};");
                AddCecilExpression($"{ctorLocalVar}.HasThis = !{ctorLocalVar}.IsStatic;", ctorLocalVar);
                return ctorLocalVar;
            }

            var ctorMethodDefinitionExp = CecilDefinitionsFactory.Constructor(Context, ctorLocalVar, typeName, isStatic, ctorAccessibility, Array.Empty<string>());
            AddCecilExpression(ctorMethodDefinitionExp);
            return ctorLocalVar;
        }

        private void ProcessFieldInitialization(TypeDeclarationSyntax declaringClass, string ctorBodyIL, bool statics)
        {
            var declaringTypeSymbol = Context.SemanticModel.GetDeclaredSymbol(declaringClass).EnsureNotNull();
            // Handles non const field initialization...
            foreach (var fieldDeclaration in NonConstFields(declaringClass, statics))
            {
                var dec = fieldDeclaration.Declaration.Variables[0];
                if (dec.Initializer == null)
                    continue;
                
                using var _ = LineInformationTracker.Track(Context, fieldDeclaration);
                Context.WriteNewLine();
                Context.WriteComment(fieldDeclaration.HumanReadableSummary());

                if (HandleSystemIndexInitialization(dec, declaringTypeSymbol, ctorBodyIL))
                    continue;
                
                if (!fieldDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))) 
                    Context.EmitCilInstruction(ctorBodyIL, OpCodes.Ldarg_0);

                if (ExpressionVisitor.Visit(Context, ctorBodyIL, dec.Initializer))
                    continue;

                var fieldVarDef = Context.DefinitionVariables.GetVariable(dec.Identifier.ValueText, VariableMemberKind.Field, declaringTypeSymbol.ToDisplayString());
                var fieldStoreOpCode = fieldDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))
                    ? OpCodes.Stsfld
                    : OpCodes.Stfld;

                Context.EmitCilInstruction(ctorBodyIL, fieldStoreOpCode, fieldVarDef.VariableName);
            }
            
            // Handles property initialization...
            foreach (var dec in declaringClass.Members.OfType<PropertyDeclarationSyntax>())
            {
                if (dec.Initializer == null)
                    continue;
                
                using var _ = LineInformationTracker.Track(Context, dec);
                Context.WriteNewLine();
                Context.WriteComment(dec.HumanReadableSummary());
             
                if (!dec.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))) 
                    Context.EmitCilInstruction(ctorBodyIL, OpCodes.Ldarg_0);

                if (ExpressionVisitor.Visit(Context, ctorBodyIL, dec.Initializer))
                    continue;

                var backingFieldVar = Context.DefinitionVariables.GetVariable(Utils.BackingFieldNameForAutoProperty(dec.Identifier.ValueText), VariableMemberKind.Field, declaringTypeSymbol.ToDisplayString());
                var fieldStoreOpCode = dec.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))
                    ? OpCodes.Stsfld
                    : OpCodes.Stfld;

                Context.EmitCilInstruction(ctorBodyIL, fieldStoreOpCode, backingFieldVar.VariableName);
            }
        }

        private static IEnumerable<FieldDeclarationSyntax> NonConstFields(TypeDeclarationSyntax type, bool statics)
        {
            return type.Members.OfType<FieldDeclarationSyntax>()
                .Where(f => f.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)) == statics && !f.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword)));
        }

        private bool HandleSystemIndexInitialization(VariableDeclaratorSyntax dec, ISymbol declaringTypeSymbol, string ctorBodyIL)
        {
            if (dec.Initializer == null || !dec.Initializer.Value.IsKind(SyntaxKind.IndexExpression))
                return false;

            // code is something like `Index field = ^5`; 
            // in this case we need to load the address of the field since the expression ^5 (IndexerExpression) will result in a call to System.Index ctor (which is a value type and expects
            // the address of the value type to be in the top of the stack
            var operand = Context.DefinitionVariables.GetVariable(dec.Identifier.Text, VariableMemberKind.Field, declaringTypeSymbol.ToDisplayString()).VariableName;
            Context.EmitCilInstruction(ctorBodyIL, OpCodes.Ldflda, operand);
            ExpressionVisitor.Visit(Context, ctorBodyIL, dec.Initializer);
            return true;
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

        private static string DefaultCtorAccessibilityFor(MemberDeclarationSyntax declaringClass, bool isStatic)
        {
            if (isStatic)
                return "MethodAttributes.Private | MethodAttributes.Static";
            
            return declaringClass.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword))
                ? "MethodAttributes.Family"
                : "MethodAttributes.Public";
        }
    }
}
