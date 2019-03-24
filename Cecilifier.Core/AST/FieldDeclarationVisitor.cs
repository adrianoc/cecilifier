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

namespace Cecilifier.Core.AST
{
	class FieldDeclarationVisitor : SyntaxWalkerBase
	{
		internal FieldDeclarationVisitor(IVisitorContext ctx) : base(ctx)
		{
		}

		public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
		{
			var declaringTypeVar = Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName;
			
			var type = ResolveType(node.Declaration.Type);
			var fieldType = ProcessRequiredModifiers(node, type) ?? type;
			var fieldAttributes = MapAttributes(node.Modifiers);
			
			var declaringType = node.ResolveDeclaringType();
			foreach (var field in node.Declaration.Variables)
			{
				var fieldVar = MethodExtensions.LocalVariableNameFor("fld", declaringType.Identifier.ValueText, field.Identifier.ValueText.CamelCase());
				var exps = CecilDefinitionsFactory.Field(declaringTypeVar, fieldVar, field.Identifier.ValueText, fieldType, fieldAttributes);
				AddCecilExpressions(exps);

				Context.DefinitionVariables.RegisterNonMethod(declaringType.Identifier.Text, field.Identifier.ValueText, MemberKind.Field, fieldVar);
			}
			base.VisitFieldDeclaration(node);
		}

	    private string ProcessRequiredModifiers(FieldDeclarationSyntax fieldDeclaration, string originalType)
	    {
	    	if (!fieldDeclaration.Modifiers.Any(m => m.Kind() == SyntaxKind.VolatileKeyword)) return null;
	    	
			var id = string.Format("mod_req{0}", NextLocalVariableId());
	    	var mod_req = string.Format("var {0} = new RequiredModifierType({1}, {2});", id, originalType, ImportExpressionForType(typeof (IsVolatile)));
	    	AddCecilExpression(mod_req);
	    	return id;
	    }

		private string MapAttributes(IEnumerable<SyntaxToken> modifiers)
        {
            var noInternalOrProtected = modifiers.Where(t => t.Kind() != SyntaxKind.InternalKeyword && t.Kind() != SyntaxKind.ProtectedKeyword);
            var str = noInternalOrProtected.Where(ExcludeHasNoCILRepresentation).Aggregate("", (acc, curr) => (acc.Length > 0  ? acc + " | " : "") + curr.MapModifier("FieldAttributes"));

			if (!modifiers.Any())
				return "FieldAttributes.Private";

			Func<SyntaxToken, bool> predicate = t => t.Kind() == SyntaxKind.InternalKeyword || t.Kind() == SyntaxKind.ProtectedKeyword;
            return modifiers.Count(predicate) == 2
									? "FieldAttributes.FamORAssem" + str
									: modifiers.Where(predicate).Select(MapAttribute).Aggregate("", (acc, curr) => "FieldAttributes." + curr) + str;
        }

        private static FieldAttributes MapAttribute(SyntaxToken token)
        {
            switch(token.Kind())
            {
                case SyntaxKind.InternalKeyword: return FieldAttributes.Assembly;
                case SyntaxKind.ProtectedKeyword: return FieldAttributes.Family;
            }

            throw new ArgumentException();
        }
	}
}
