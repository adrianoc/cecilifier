using System.Linq;
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
			foreach (var field in node.Declaration.Variables)
			{
				var fieldAttributes = FieldModifiersToCecil(node);

				var type = ResolveLocalVariable(node.Declaration.Type.PlainName) ?? ResolveType(node.Declaration.Type);
				var fieldId = string.Format("ft{0}", NextLocalVariableId());
				var fieldType = ProcessRequiredModifiers(node, type) ?? type;
				var fieldDeclaration = string.Format("var {0} = new FieldDefinition(\"{1}\", {2}, {3});",
																fieldId,
																field.Identifier.Value,
																fieldAttributes,
																fieldType);

				AddCecilExpression(fieldDeclaration);
				AddCecilExpression("{0}.Fields.Add({1});", ResolveLocalVariable(node.Parent.ResolveDeclaringType()), fieldId);
			}

			base.VisitFieldDeclaration(node);
		}

		private static string FieldModifiersToCecil(FieldDeclarationSyntax node)
		{
			return ModifiersToCecil("FieldAttributes", node.Modifiers, string.Empty);
		}

		private string ProcessRequiredModifiers(FieldDeclarationSyntax fieldDeclaration, string originalType)
		{
			if (fieldDeclaration.Modifiers.Any(m => m.ContextualKind == SyntaxKind.VolatileKeyword))
			{
				var id = string.Format("mod_req{0}", NextLocalVariableId());
				var mod_req = string.Format("var {0} = new RequiredModifierType({1}, {2});", id, originalType, ImportExpressionFor(typeof(IsVolatile)));
				AddCecilExpression(mod_req);
				return id;
			}

			return null;
		}

	}
}
