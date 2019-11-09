using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpExtensions = Microsoft.CodeAnalysis.CSharp.CSharpExtensions;

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
            var declaringType = node.ResolveDeclaringType();

            HandleFieldDeclaration(node, variableDeclarationSyntax, modifiers, declaringType);
            
            base.VisitFieldDeclaration(node);
        }

        internal static IEnumerable<string> HandleFieldDeclaration(IVisitorContext context, MemberDeclarationSyntax node, VariableDeclarationSyntax variableDeclarationSyntax,
            SyntaxTokenList modifiers, BaseTypeDeclarationSyntax declaringType)
        {
            var visitor = new FieldDeclarationVisitor(context);
            return visitor.HandleFieldDeclaration(node, variableDeclarationSyntax, modifiers, declaringType);
        }

        private IEnumerable<string> HandleFieldDeclaration(MemberDeclarationSyntax node, VariableDeclarationSyntax variableDeclarationSyntax, SyntaxTokenList modifiers, BaseTypeDeclarationSyntax declaringType)
        {
            var declaringTypeVar = Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName;

            var fieldDefVars = new List<string>(variableDeclarationSyntax.Variables.Count);
            
            var type = ResolveType(variableDeclarationSyntax.Type);
            var fieldType = ProcessRequiredModifiers(modifiers, type) ?? type;
            var fieldAttributes = MapAttributes(modifiers);

            foreach (var field in variableDeclarationSyntax.Variables)
            {
                var fieldVar = MethodExtensions.LocalVariableNameFor("fld", declaringType.Identifier.ValueText, field.Identifier.ValueText.CamelCase());
                fieldDefVars.Add(fieldVar);
                
                var exps = CecilDefinitionsFactory.Field(declaringTypeVar, fieldVar, field.Identifier.ValueText, fieldType, fieldAttributes);
                AddCecilExpressions(exps);
                
                HandleAttributesInMemberDeclaration(node.AttributeLists, fieldVar);

                Context.DefinitionVariables.RegisterNonMethod(declaringType.Identifier.Text, field.Identifier.ValueText, MemberKind.Field, fieldVar);
            }

            return fieldDefVars;
        }

        private string ProcessRequiredModifiers(SyntaxTokenList modifiers, string originalType)
        {
            if (modifiers.All(m => m.Kind() != SyntaxKind.VolatileKeyword))
            {
                return null;
            }

            var id = string.Format("mod_req{0}", NextLocalVariableId());
            var mod_req = string.Format("var {0} = new RequiredModifierType({1}, {2});", id, originalType, ImportExpressionForType(typeof(IsVolatile)));
            AddCecilExpression(mod_req);
            return id;
        }

        private string MapAttributes(IEnumerable<SyntaxToken> modifiers)
        {
            var noInternalOrProtected = modifiers.Where(t => t.Kind() != SyntaxKind.InternalKeyword && t.Kind() != SyntaxKind.ProtectedKeyword);
            var str = noInternalOrProtected.Where(ExcludeHasNoCILRepresentation).Aggregate("", (acc, curr) => (acc.Length > 0 ? acc + " | " : "") + curr.MapModifier("FieldAttributes"));

            if (!modifiers.Any())
            {
                return "FieldAttributes.Private";
            }

            Func<SyntaxToken, bool> predicate = t => t.Kind() == SyntaxKind.InternalKeyword || t.Kind() == SyntaxKind.ProtectedKeyword;
            return modifiers.Count(predicate) == 2
                ? "FieldAttributes.FamORAssem" + str
                : modifiers.Where(predicate).Select(MapAttribute).Aggregate("", (acc, curr) => "FieldAttributes." + curr) + str;
        }

        private static FieldAttributes MapAttribute(SyntaxToken token)
        {
            switch (token.Kind())
            {
                case SyntaxKind.InternalKeyword: return FieldAttributes.Assembly;
                case SyntaxKind.ProtectedKeyword: return FieldAttributes.Family;
            }

            throw new ArgumentException();
        }
    }
}
