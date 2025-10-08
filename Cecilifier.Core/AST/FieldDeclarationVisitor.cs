using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
    internal class FieldDeclarationVisitor : SyntaxWalkerBase
    {
        internal FieldDeclarationVisitor(IVisitorContext ctx) : base(ctx)
        {
        }

        public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            var memberDeclarationSyntax = (MemberDeclarationSyntax) node.Parent!.Parent;
            var modifiers = memberDeclarationSyntax!.Modifiers;
            var declaringType = memberDeclarationSyntax.ResolveDeclaringType<TypeDeclarationSyntax>();

            HandleFieldDeclaration(memberDeclarationSyntax, (VariableDeclarationSyntax)node.Parent, modifiers, declaringType);

            base.VisitVariableDeclarator(node);
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            var variableDeclarationSyntax = node.Declaration;
            var modifiers = node.Modifiers;
            var declaringType = node.ResolveDeclaringType<TypeDeclarationSyntax>();

            HandleFieldDeclaration(node, variableDeclarationSyntax, modifiers, declaringType);

            base.VisitFieldDeclaration(node);
        }

        internal static IEnumerable<string> HandleFieldDeclaration(IVisitorContext context, MemberDeclarationSyntax node, VariableDeclarationSyntax variableDeclarationSyntax,
            IReadOnlyList<SyntaxToken> modifiers, BaseTypeDeclarationSyntax declaringType)
        {
            var visitor = new FieldDeclarationVisitor(context);
            return visitor.HandleFieldDeclaration(node, variableDeclarationSyntax, modifiers, declaringType);
        }

        private IEnumerable<string> HandleFieldDeclaration(MemberDeclarationSyntax node, VariableDeclarationSyntax variableDeclarationSyntax, IReadOnlyList<SyntaxToken> modifiers, BaseTypeDeclarationSyntax declaringType)
        {
            var declaringTypeVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Type);
            var declaringTypeSymbol = Context.SemanticModel.GetDeclaredSymbol(declaringType).EnsureNotNull();

            var fieldDefVars = new List<string>(variableDeclarationSyntax.Variables.Count);

            var fieldType = ResolveTypeSymbol(variableDeclarationSyntax.Type);
            var fieldAttributes = ModifiersToCecil<FieldAttributes>(modifiers, "Private", MapFieldAttributesFor);
            var isByRef = variableDeclarationSyntax.Type is RefTypeSyntax;

            foreach (var field in variableDeclarationSyntax.Variables)
            {
                using var _ = LineInformationTracker.Track(Context, field);
                
                var fieldSymbol = Context.SemanticModel.GetDeclaredSymbol(field).EnsureNotNull();
                // skip field already processed due to forward references.
                var fieldDeclarationVariable = Context.DefinitionVariables.GetVariable(field.Identifier.Text, VariableMemberKind.Field, declaringTypeSymbol.ToDisplayString());
                if (fieldDeclarationVariable.IsValid)
                    continue;

                var fieldVar = Context.Naming.FieldDeclaration(node);
                fieldDefVars.Add(fieldVar);
                var constant = modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword)) && field.Initializer != null ? Context.SemanticModel.GetConstantValue(field.Initializer.Value) : null;
                var exps = Context.ApiDefinitionsFactory.Field(
                                                            Context, 
                                                            new MemberDefinitionContext(fieldVar, declaringTypeVar.VariableName, MemberOptions.None, IlContext.None), 
                                                            fieldSymbol, 
                                                            fieldType,
                                                            fieldAttributes, 
                                                            modifiers.Any(m => m.IsKind(SyntaxKind.VolatileKeyword)),
                                                            isByRef,
                                                            constant.Value.ValueText());
                AddCecilExpressions(Context, exps);
                HandleAttributesInMemberDeclaration(node.AttributeLists, fieldVar);
            }

            return fieldDefVars;
        }

        internal static IEnumerable<string> MapFieldAttributesFor(SyntaxToken token) =>
            token.Kind() switch
            {
                SyntaxKind.InternalKeyword => new[] { "Assembly" },
                SyntaxKind.ProtectedKeyword => new[] { "Family" },
                SyntaxKind.PrivateKeyword => new[] { "Private" },
                SyntaxKind.PublicKeyword => new[] { "Public" },
                SyntaxKind.StaticKeyword => new[] { "Static" },
                SyntaxKind.AbstractKeyword => new[] { "Abstract" },
                SyntaxKind.ConstKeyword => new[] { "Literal", "Static" },
                SyntaxKind.ReadOnlyKeyword => new[] { "InitOnly" },
                SyntaxKind.VolatileKeyword => Array.Empty<string>(),

                _ => throw new ArgumentException($"Unsupported attribute name: {token.Kind().ToString()}")
            };
    }
}
