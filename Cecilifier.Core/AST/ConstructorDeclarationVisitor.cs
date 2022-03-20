using System;
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
                    Context.EmitCilInstruction(ilVar, OpCodes.Ldarg_0);
                }
               
                // If this ctor has an initializer the call to the base ctor will happen when we visit call base.VisitConstructorDeclaration()
                // otherwise we need to call it here.
                if (node.Initializer == null && declaringType.Kind() != SyntaxKind.StructDeclaration)
                {
                    var declaringTypeLocalVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Type).VariableName;
                    string operand = Utils.ImportFromMainModule($"TypeHelpers.DefaultCtorFor({declaringTypeLocalVar}.BaseType)");
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
            return string.Empty.AppendModifier(CtorFlags);
        }

        internal void DefaultCtorInjector(string typeDefVar, ClassDeclarationSyntax declaringClass)
        {
            DefaultCtorInjector(typeDefVar, declaringClass.Identifier.Text, DefaultCtorAccessibilityFor(declaringClass), ResolveDefaultCtorFor(typeDefVar, declaringClass), ctorBodyIL =>
            {
                ProcessFieldInitialization(declaringClass, ctorBodyIL);
            });
        }

        internal void DefaultCtorInjector(string typeDefVar, string typeName, string ctorAccessibility, string baseCtor, Action<string> processInitializers)
        {
            Context.WriteNewLine();
            Context.WriteComment($"** Constructor: {typeName}() **");
            
            var ctorLocalVar = AddOrUpdateParameterlessCtorDefinition(
                typeName,
                ctorAccessibility,
                Context.Naming.SyntheticVariable("ctor", ElementKind.Constructor));
                                    
            AddCecilExpression($"{typeDefVar}.Methods.Add({ctorLocalVar});");

            var ctorBodyIL = Context.Naming.ILProcessor("ctor");
            AddCecilExpression($@"var {ctorBodyIL} = {ctorLocalVar}.Body.GetILProcessor();");
            
            processInitializers?.Invoke(ctorBodyIL);
            Context.EmitCilInstruction(ctorBodyIL, OpCodes.Ldarg_0);
            Context.EmitCilInstruction(ctorBodyIL, OpCodes.Call, baseCtor);
            
            Context.EmitCilInstruction(ctorBodyIL, OpCodes.Ret);
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
            var declaringTypeSymbol = Context.SemanticModel.GetDeclaredSymbol(declaringClass).EnsureNotNull();
            // Handles non const field initialization...
            foreach (var fieldDeclaration in declaringClass.Members.OfType<FieldDeclarationSyntax>().Where(f => !f.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword))))
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

        private static string DefaultCtorAccessibilityFor(BaseTypeDeclarationSyntax declaringClass)
        {
            return declaringClass.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword))
                ? "MethodAttributes.Family"
                : "MethodAttributes.Public";
        }
    }
}
