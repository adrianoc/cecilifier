using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cecilifier.Core.Extensions;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
	class FieldDeclarationVisitor : SyntaxWalkerBase
	{
		internal FieldDeclarationVisitor(IVisitorContext ctx) : base(ctx)
		{
		}

		protected override void VisitFieldDeclaration(FieldDeclarationSyntax node)
		{
			var type = ResolveType(node.Declaration.Type);
			var fieldType = ProcessRequiredModifiers(node, type) ?? type;
			var fieldAttributes = MapAttributes(node.Modifiers);
			
			foreach (var field in node.Declaration.Variables)
			{
				var fieldId = LocalVariableNameFor("fld", node.ResolveDeclaringType().Identifier.ValueText, field.Identifier.ValueText.CamelCase());
				var fieldDeclaration = string.Format("var {0} = new FieldDefinition(\"{1}\", {2}, {3});",
																fieldId,
																field.Identifier.Value,
																fieldAttributes,
																fieldType);
				AddCecilExpression(fieldDeclaration);
				AddCecilExpression("{0}.Fields.Add({1});", ResolveTypeLocalVariable(node.Parent.ResolveDeclaringType()), fieldId);
			}

			base.VisitFieldDeclaration(node);
		}

	    private string ProcessRequiredModifiers(FieldDeclarationSyntax fieldDeclaration, string originalType)
	    {
	    	if (!fieldDeclaration.Modifiers.Any(m => m.ContextualKind == SyntaxKind.VolatileKeyword)) return null;
	    	
			var id = string.Format("mod_req{0}", NextLocalVariableId());
	    	var mod_req = string.Format("var {0} = new RequiredModifierType({1}, {2});", id, originalType, ImportExpressionFor(typeof (IsVolatile)));
	    	AddCecilExpression(mod_req);
	    	return id;
	    }

		protected string MapAttributes(IEnumerable<SyntaxToken> modifiers)
        {
            var noInternalOrProtected = modifiers.Where(t => t.Kind != SyntaxKind.InternalKeyword && t.Kind != SyntaxKind.ProtectedKeyword);
            var str = noInternalOrProtected.Where(ExcludeHasNoCILRepresentation).Aggregate("",
                (acc, curr) => (acc.Length > 0 
                                    ? acc + " | " 
                                    : "") + curr.MapModifier("FieldAttributes"));

            Func<SyntaxToken, bool> predicate = t => t.Kind == SyntaxKind.InternalKeyword || t.Kind == SyntaxKind.ProtectedKeyword;
            return
                modifiers.Count(predicate) == 2
                    ? "FieldAttributes.FamORAssem" + str
                    : modifiers.Where(predicate).Select(MapAttribute).Aggregate("", (acc, curr) => "FieldAttributes." + curr) + str;
        }

        private static FieldAttributes MapAttribute(SyntaxToken token)
        {
            switch(token.Kind)
            {
                case SyntaxKind.InternalKeyword: return FieldAttributes.Assembly;
                case SyntaxKind.ProtectedKeyword: return FieldAttributes.Family;
            }

            throw new ArgumentException();
        }
	}
}
