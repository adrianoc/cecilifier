using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
    internal class EnumDeclarationVisitor : TypeDeclarationVisitorBase
    {
        private EnumMemberValueCollector _memberCollector;
        public EnumDeclarationVisitor(IVisitorContext context) : base(context)
        {
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            _memberCollector = new EnumMemberValueCollector();
            node.Accept(_memberCollector);
            
            var enumType = TempLocalVar(node.Identifier.ValueText);
            var attrs = ModifiersToCecil("TypeAttributes", node.Modifiers, "Private");
            var exps = CecilDefinitionsFactory.Type(Context, enumType, node.Identifier.ValueText, attrs + " | TypeAttributes.Sealed", ResolveType("System.Enum"), false, new string[0]);
            AddCecilExpressions(exps);

            using(Context.DefinitionVariables.WithCurrent(node.Parent.IsKind(SyntaxKind.CompilationUnit) ? "" : node.Parent.ResolveDeclaringType().Identifier.ValueText, node.Identifier.ValueText, MemberKind.Type, enumType))
            {
                //.class private auto ansi MyEnum
                //TODO: introduce TypeSystem.CoreLib.Enum/Action/etc...
				
                var fieldVar = MethodExtensions.LocalVariableNameFor("valueField", node.Identifier.ValueText);
                var valueFieldExp = CecilDefinitionsFactory.Field(enumType, fieldVar, "value__", "assembly.MainModule.TypeSystem.Int32", "FieldAttributes.SpecialName | FieldAttributes.RTSpecialName | FieldAttributes.Public");
                AddCecilExpressions(valueFieldExp);
                
                HandleAttributesInTypeDeclaration(node, enumType);
				
                base.VisitEnumDeclaration(node);
            }
        }

        public override void VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node)
        {
            // Adds a field like:
            // .field public static literal valuetype xxx.MyEnum Second = int32(1)
            var enumMemberValue = _memberCollector[node];
            var enumVar = Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName;
			
            var fieldVar = MethodExtensions.LocalVariableNameFor($"em_{Context.DefinitionVariables.Current.MemberName}_{NextLocalVariableId()}", node.Identifier.ValueText);
            var exp  = CecilDefinitionsFactory.Field(enumVar, fieldVar, node.Identifier.ValueText, enumVar, "FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.Public | FieldAttributes.HasDefault", $"Constant = {enumMemberValue}");
            AddCecilExpressions(exp);

            base.VisitEnumMemberDeclaration(node);
        }

        private class EnumMemberValueCollector : CSharpSyntaxVisitor<int>
        {
            private int _lastEnumMemberValue = -1;
            private EnumDeclarationSyntax _enum;
            private Dictionary<EnumMemberDeclarationSyntax, int> _dict  = new Dictionary<EnumMemberDeclarationSyntax, int>();

            public int this[EnumMemberDeclarationSyntax member] => _dict[member];

            public override int VisitEnumDeclaration(EnumDeclarationSyntax node)
            {
                _enum = node;
                foreach (var member in node.Members)
                {
                    member.Accept(this);
                }

                return 0;
            }

            public override int VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node)
            {
                if (_dict.TryGetValue(node, out var existingValue))
                {
                    // We may have already visited this due to some other enum member referencing this one before its declaration.
                    _lastEnumMemberValue = existingValue;
                    return existingValue;
                }
                
                int value;
                if (node.EqualsValue != null)
                {
                    value = node.EqualsValue.Value.Accept(this);
                }
                else
                {
                    value = _lastEnumMemberValue + 1;
                }

                _dict[node] = value;
                _lastEnumMemberValue = value;
                
                return value;
            }

            public override int VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                if (node.Kind() != SyntaxKind.NumericLiteralExpression)
                    throw new InvalidOperationException($"Invalid literal type: {node}");
                
                var value = node.ToString();
                if (value.StartsWith("0x"))
                {
                    return Convert.ToInt32(value.Substring(2), 16);
                }

                return Int32.Parse(value);
            }

            public override int VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                var leftValue = node.Left.Accept(this);
                var rightValue = node.Right.Accept(this);
                
                switch (node.OperatorToken.Kind())
                {
                    case SyntaxKind.PlusToken: return leftValue + rightValue;
                    case SyntaxKind.MinusToken: return leftValue - rightValue;
                    case SyntaxKind.AsteriskToken: return leftValue * rightValue;
                }
                
                throw new InvalidOperationException($"Operator {node.OperatorToken} is not supported yet as enum member initializer");
            }

            public override int VisitIdentifierName(IdentifierNameSyntax node)
            {
                var enumMember = _enum.Members.SingleOrDefault(em => em.Identifier.ValueText == node.Identifier.ValueText);
                if (!_dict.TryGetValue(enumMember, out var value))
                {
                    enumMember.Accept(this);
                }
                
                return _dict[enumMember];
            }
        }
    }
}