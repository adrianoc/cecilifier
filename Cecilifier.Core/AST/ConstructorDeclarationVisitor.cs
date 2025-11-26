using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.ApiDriver.Handles;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;

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
                {
                    ProcessFieldInitialization(declaringType, ilVar, false);
                }

                if (declaringType.Kind() != SyntaxKind.StructDeclaration)
                {
                    Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Ldarg_0);
                }

                // If this ctor has an initializer the call to the base ctor will happen when we visit call base.VisitConstructorDeclaration()
                // otherwise we need to call it here.
                if (node.Initializer == null && declaringType.Kind() != SyntaxKind.StructDeclaration)
                {
                    var baseTypeSymbol = Context.RoslynTypeSystem.SystemObject;
                    if (declaringType.BaseList != null)
                    {
                        // Assumes the first type in a base list is an actual type.
                        var baseTypeSyntax = declaringType.BaseList.Types.First();
                        var type = Context.SemanticModel.GetTypeInfo(baseTypeSyntax.Type).Type.EnsureNotNull();
                        if (type.TypeKind != TypeKind.Interface)
                            baseTypeSymbol = type;
                    }
                    
                    var declaringTypeLocalVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Type).ThrowIfVariableIsNotValid();
                    var operand = Context.MemberResolver.ResolveDefaultConstructor(baseTypeSymbol, declaringTypeLocalVar.VariableName);
                    Context.ApiDriver.WriteCilInstruction(Context, ilVar, OpCodes.Call, operand.AsToken());
                }

                callBaseMethod(node);
            });
        }

        public override void VisitConstructorInitializer(ConstructorInitializerSyntax node)
        {
            new ConstructorInitializerVisitor(Context, ilVar).Visit(node);
        }

        protected override string GetSpecificModifiers() => Constants.Cecil.CtorAttributes;

        internal void DefaultCtorInjector(string typeDefVar, ClassDeclarationSyntax declaringClass, bool isStatic)
        {
            var typeSymbol = Context.SemanticModel.GetDeclaredSymbol(declaringClass).EnsureNotNull();
            DefaultCtorInjector(typeDefVar,  typeSymbol, DefaultCtorAccessibilityFor(declaringClass, isStatic), ResolveDefaultCtorFor(typeDefVar, declaringClass), isStatic, ctorBodyIL =>
            {
                ProcessFieldInitialization(declaringClass, ctorBodyIL, isStatic);
            });
        }

        private void DefaultCtorInjector(string typeDefVar, INamedTypeSymbol type, string ctorAccessibility, string baseCtor, bool isStatic, Action<IlContext> processInitializers)
        {
            DefaultCtorInjector(typeDefVar, type.Name, type.OriginalDefinition.ToDisplayString(), ctorAccessibility, baseCtor, isStatic, processInitializers);
        }

        internal void DefaultCtorInjector(string typeDefVar, string typeName, string ctorAccessibility, string baseCtor, bool isStatic, Action<IlContext> processInitializers)
        {
            DefaultCtorInjector(typeDefVar, typeName, typeName, ctorAccessibility, baseCtor, isStatic, processInitializers);
        }

        /// <param name="typeDefVar"></param>
        /// <param name="normalizedTypeName">The type name without any symbols (such as angle brackets) considered invalid in variable names</param>
        /// <param name="typeName">The symbol original definition name. For instance the generic type Gen&lt;T&gt; has a <paramref name="typeName"/> of Gen&lt;&gt;</param>
        /// <param name="ctorAccessibility"></param>
        /// <param name="baseCtor"></param>
        /// <param name="isStatic"></param>
        /// <param name="processInitializers">Action in charge of handling constructor initializers</param>
        private void DefaultCtorInjector(string typeDefVar, string normalizedTypeName, string typeName, string ctorAccessibility, string baseCtor, bool isStatic, Action<IlContext> processInitializers)
        {
            Context.WriteNewLine();
            Context.WriteComment($"** Constructor: {normalizedTypeName}() **");

            var ilContext = AddOrUpdateParameterlessCtorDefinition(
                typeName,
                normalizedTypeName,
                typeDefVar,
                ctorAccessibility,
                isStatic,
                Context.Naming.SyntheticVariable(normalizedTypeName, isStatic ? ElementKind.StaticConstructor : ElementKind.Constructor));

            processInitializers?.Invoke(ilContext);
            
            if (!isStatic)
            {
                Context.ApiDriver.WriteCilInstruction(Context, ilContext, OpCodes.Ldarg_0);
                Context.ApiDriver.WriteCilInstruction(Context, ilContext, OpCodes.Call, baseCtor.AsToken());
            }

            Context.ApiDriver.WriteCilInstruction(Context, ilContext, OpCodes.Ret);
        }

        private IlContext AddOrUpdateParameterlessCtorDefinition(string typeName, string normalizedTypeName, string typeDefVar, string ctorAccessibility, bool isStatic, string ctorLocalVar)
        {
            var ctorName = isStatic ? "cctor" : "ctor";
            var found = Context.DefinitionVariables.GetMethodVariable(new MethodDefinitionVariable(typeName, Utils.ConstructorMethodName(isStatic), [], 0));
            if (found.IsValid)
            {
                //TODO: This is Cecil specific. Abstract it and add a test in SRM that exercises it
                ctorLocalVar = found.VariableName;

                AddCecilExpression($"{ctorLocalVar}.Attributes = {ctorAccessibility} | MethodAttributes.HideBySig | {Constants.Cecil.CtorAttributes};");
                AddCecilExpression($"{ctorLocalVar}.HasThis = !{ctorLocalVar}.IsStatic;", ctorLocalVar);
                
                //TODO: This code was in the caller before and we moved it to `DefinitionFactory.Constructor()`
                //      the problem is that `CecilifierContextExtensions.EnsureForwardedMethod()` will emit a 
                //      method definition for the constructor and assume the caller will add it to the type,
                //      which now does not happen anymore. Probably we want to move EnsureForwardedMethod()
                //      to the factory class and add the method definition to the type there. For now just
                //      add it here (this will break tests for SRM driver).
                AddCecilExpression($"{typeDefVar}.Methods.Add({ctorLocalVar});");
                
                return Context.ApiDriver.NewIlContext(Context, $"{ctorName}_{normalizedTypeName}", ctorLocalVar);
            }

            var ilContext = Context.ApiDriver.NewIlContext(Context, $"{ctorName}_{normalizedTypeName}", ctorLocalVar);
            var definitionContext = new BodiedMemberDefinitionContext(ctorName, ctorName, ctorLocalVar, typeDefVar, MemberOptions.None, ilContext);
            var exps = Context.ApiDefinitionsFactory.Constructor(Context, definitionContext, typeName, isStatic, ctorAccessibility, []);
            AddCecilExpressions(Context, exps);

            return ilContext;
        }

        private void ProcessFieldInitialization(TypeDeclarationSyntax declaringClass, IlContext ctorBodyIL, bool statics)
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
                    Context.ApiDriver.WriteCilInstruction(Context, ctorBodyIL, OpCodes.Ldarg_0);

                if (ExpressionVisitor.Visit(Context, ctorBodyIL, dec.Initializer))
                    continue;

                var fieldVarDef = Context.DefinitionVariables.GetVariable(dec.Identifier.ValueText, VariableMemberKind.Field, declaringTypeSymbol.ToDisplayString());
                var fieldStoreOpCode = fieldDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))
                    ? OpCodes.Stsfld
                    : OpCodes.Stfld;

                Debug.Assert(fieldVarDef.IsValid);
                Context.ApiDriver.WriteCilInstruction(Context, ctorBodyIL, fieldStoreOpCode, fieldVarDef.VariableName.AsToken());
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
                    Context.ApiDriver.WriteCilInstruction(Context, ctorBodyIL, OpCodes.Ldarg_0);

                if (ExpressionVisitor.Visit(Context, ctorBodyIL, dec.Initializer))
                    continue;

                var backingFieldVar = Context.DefinitionVariables.GetVariable(Utils.BackingFieldNameForAutoProperty(dec.Identifier.ValueText), VariableMemberKind.Field, declaringTypeSymbol.ToDisplayString());
                var fieldStoreOpCode = dec.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))
                    ? OpCodes.Stsfld
                    : OpCodes.Stfld;

                Context.ApiDriver.WriteCilInstruction(Context, ctorBodyIL, fieldStoreOpCode, backingFieldVar.VariableName);
            }
        }

        private static IEnumerable<FieldDeclarationSyntax> NonConstFields(TypeDeclarationSyntax type, bool statics)
        {
            return type.Members.OfType<FieldDeclarationSyntax>()
                .Where(f => f.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)) == statics && !f.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword)));
        }

        private bool HandleSystemIndexInitialization(VariableDeclaratorSyntax dec, ISymbol declaringTypeSymbol, IlContext il)
        {
            if (dec.Initializer == null || !dec.Initializer.Value.IsKind(SyntaxKind.IndexExpression))
                return false;

            // code is something like `Index field = ^5`; 
            // in this case we need to load the address of the field since the expression ^5 (IndexerExpression) will result in a call to System.Index ctor (which is a value type and expects
            // the address of the value type to be in the top of the stack
            var operand = Context.DefinitionVariables.GetVariable(dec.Identifier.Text, VariableMemberKind.Field, declaringTypeSymbol.ToDisplayString()).VariableName;
            Context.ApiDriver.WriteCilInstruction(Context, il, OpCodes.Ldflda, operand.AsToken());
            ExpressionVisitor.Visit(Context, il, dec.Initializer);
            return true;
        }

        private string ResolveDefaultCtorFor(string typeDefVar, BaseTypeDeclarationSyntax type)
        {
            var typeSymbol = Context.GetDeclaredSymbol(type);
            if (typeSymbol == null)
                return Utils.ImportFromMainModule($"TypeHelpers.DefaultCtorFor({typeDefVar}.BaseType)");

            return Context.MemberResolver.ResolveDefaultConstructor(typeSymbol.BaseType, typeDefVar);
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
