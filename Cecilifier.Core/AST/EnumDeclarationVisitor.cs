using System;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
    internal class EnumDeclarationVisitor : SyntaxWalkerBase
    {
        private int lastMemberValue = 0;
        public EnumDeclarationVisitor(IVisitorContext context) : base(context)
        {
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            var enumType = TempLocalVar(node.Identifier.ValueText);
            var attrs = ModifiersToCecil("TypeAttributes", node.Modifiers, "Private");
            var exps = CecilDefinitionsFactory.Type(Context, enumType, node.Identifier.ValueText, attrs + " | TypeAttributes.Sealed", ResolveType("System.Enum"), false, new string[0]);
            AddCecilExpressions(exps);

            using (Context.BeginType(node.Identifier.ValueText))
            {
                //.class private auto ansi MyEnum
                //TODO: introduce TypeSystem.CoreLib.Enum/Action/etc...
				
                var fieldVar = MethodExtensions.LocalVariableNameFor("valueField", node.Identifier.ValueText);
                var valueFieldExp = CecilDefinitionsFactory.Field(enumType, fieldVar, "value__", "assembly.MainModule.TypeSystem.Int32", "FieldAttributes.SpecialName | FieldAttributes.RTSpecialName | FieldAttributes.Public");
                AddCecilExpressions(valueFieldExp);
				
                base.VisitEnumDeclaration(node);
            }
        }

        public override void VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node)
        {
            // Adds a field like:
            // .field public static literal valuetype xxx.MyEnum Second = int32(1)

            var enumVar = ResolveTypeLocalVariable(((EnumDeclarationSyntax) node.Parent).Identifier.ValueText);
			
            var enumMemberValue = node.EqualsValue?.Accept(new EnumConstantExtractor()) ?? lastMemberValue;
            var fieldVar = MethodExtensions.LocalVariableNameFor($"em_{Context.CurrentType}_{NextLocalVariableId()}", node.Identifier.ValueText);
            var exp  = CecilDefinitionsFactory.Field(enumVar, fieldVar, node.Identifier.ValueText, enumVar, "FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.Public", $"Constant = {enumMemberValue}");
            AddCecilExpressions(exp);

            lastMemberValue = enumMemberValue + 1;
            base.VisitEnumMemberDeclaration(node);
        }

        private class EnumConstantExtractor : CSharpSyntaxVisitor<int>
        {
            public override int VisitEqualsValueClause(EqualsValueClauseSyntax node)
            {
                return node.Value.Accept(this);
            }

            public override int VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                if (node.Kind() == SyntaxKind.NumericLiteralExpression)
                {
                    var value = node.ToString();
                    if (value.StartsWith("0x"))
                        return Convert.ToInt32(value.Substring(2), 16);
				
                    return Int32.Parse(value);
                }
			
                throw new InvalidOperationException($"Invalid literal type: {node}");
            }
        }
    }
}