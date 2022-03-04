using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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

            var fieldDefVars = new List<string>(variableDeclarationSyntax.Variables.Count);
            
            var type = ResolveType(variableDeclarationSyntax.Type);
            var fieldType = ProcessRequiredModifiers(modifiers, type) ?? type;
            var fieldAttributes = ModifiersToCecil(modifiers, "FieldAttributes", "Private");

            foreach (var field in variableDeclarationSyntax.Variables)
            {
                using var _ = LineInformationTracker.Track(Context, field);
                // skip field already processed due to forward references.
                var fieldDeclarationVariable = Context.DefinitionVariables.GetVariable(field.Identifier.Text, VariableMemberKind.Field, declaringType.Identifier.Text);
                if (fieldDeclarationVariable.IsValid)
                    continue;

                var fieldVar = Context.Naming.FieldDeclaration(node);
                fieldDefVars.Add(fieldVar);
                var constant = modifiers.Any( m => m.IsKind(SyntaxKind.ConstKeyword)) && field.Initializer != null ? Context.SemanticModel.GetConstantValue(field.Initializer.Value) : null;
                var exps = CecilDefinitionsFactory.Field(Context, declaringTypeVar.MemberName, declaringTypeVar.VariableName, fieldVar, field.Identifier.ValueText, fieldType, fieldAttributes, constant.Value.ValueText());
                AddCecilExpressions(exps);
                
                HandleAttributesInMemberDeclaration(node.AttributeLists, fieldVar);
            }

            return fieldDefVars;
        }

        private string ProcessRequiredModifiers(IReadOnlyList<SyntaxToken> modifiers, string originalType)
        {
            if (modifiers.All(m => m.Kind() != SyntaxKind.VolatileKeyword))
                return null;

            var id = Context.Naming.RequiredModifier();
            var mod_req = $"var {id} = new RequiredModifierType({ImportExpressionForType(typeof(IsVolatile))}, {originalType});";
            AddCecilExpression(mod_req);
            
            return id;
        }
    }
}
