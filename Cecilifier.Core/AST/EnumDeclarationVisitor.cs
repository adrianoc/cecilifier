using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
    internal class EnumDeclarationVisitor : SyntaxWalkerBase
    {
        private EnumMemberValueCollector _memberCollector;

        public EnumDeclarationVisitor(IVisitorContext context) : base(context)
        {
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            Context.WriteComment($"Enum: {node.Identifier}");
            _memberCollector = new EnumMemberValueCollector();
            node.Accept(_memberCollector);

            var enumSymbol = Context.SemanticModel.GetDeclaredSymbol(node).EnsureNotNull<ISymbol, INamedTypeSymbol>($"Something really bad happened. Roslyn failed to resolve the symbol for the enum {node.Identifier.Text}");
            var outerTypeVariable = Context.DefinitionVariables.GetVariable(enumSymbol.ContainingType?.ToDisplayString(), VariableMemberKind.Type, enumSymbol.ContainingType?.ContainingSymbol.ToDisplayString());
            var enumTypeVariable = Context.Naming.Type(node);
            var typeDef = Context.ApiDefinitionsFactory.Type(
                                                        Context, 
                                                        enumTypeVariable, 
                                                        enumSymbol.ContainingNamespace?.FullyQualifiedName() ?? string.Empty, 
                                                        enumSymbol.Name, 
                                                        TypeModifiersToCecil(enumSymbol, node.Modifiers) + " | TypeAttributes.Sealed", 
                                                        Context.TypeResolver.Bcl.System.Enum, 
                                                        outerTypeVariable, 
                                                        false, 
                                                        Array.Empty<ITypeSymbol>(), 
                                                        [], 
                                                        [], 
                                                        new string[0]);
            AddCecilExpressions(Context, typeDef);

            var parentName = enumSymbol.ContainingSymbol.ToDisplayString();
            using (Context.DefinitionVariables.WithCurrent(parentName, enumSymbol.FullyQualifiedName(), VariableMemberKind.Type, enumTypeVariable))
            {
                //.class private auto ansi MyEnum
                var fieldVar = Context.Naming.LocalVariable(node);
                var valueFieldExp = CecilDefinitionsFactory.Field(
                                                            Context, 
                                                            enumSymbol.ToDisplayString(), 
                                                            enumTypeVariable, 
                                                            fieldVar, 
                                                            "value__", 
                                                            Context.TypeResolver.Bcl.System.Int32,
                                                            "FieldAttributes.SpecialName | FieldAttributes.RTSpecialName | FieldAttributes.Public",
                                                            isByRef: false);
                AddCecilExpressions(Context, valueFieldExp);

                HandleAttributesInMemberDeclaration(node.AttributeLists, enumTypeVariable);

                base.VisitEnumDeclaration(node);
            }
        }

        public override void VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node)
        {
            using var _ = LineInformationTracker.Track(Context, node);
            // Adds a field like:
            // .field public static literal valuetype xxx.MyEnum Second = int32(1)
            var enumMemberValue = _memberCollector[node];
            var enumVarDef = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Type);

            var enumMemberSymbol = Context.SemanticModel.GetDeclaredSymbol(node).EnsureNotNull();
            var fieldVar = Context.Naming.LocalVariable(node);
            var exp = CecilDefinitionsFactory.Field(Context, enumMemberSymbol.ContainingSymbol.ToDisplayString(), enumVarDef.VariableName, fieldVar, node.Identifier.ValueText, enumVarDef.VariableName,
                "FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.Public | FieldAttributes.HasDefault", isByRef: false, enumMemberValue);
            AddCecilExpressions(Context, exp);

            HandleAttributesInMemberDeclaration(node.AttributeLists, fieldVar);

            base.VisitEnumMemberDeclaration(node);
        }

        private class EnumMemberValueCollector : CSharpSyntaxVisitor<int>
        {
            private readonly Dictionary<EnumMemberDeclarationSyntax, int> _dict = new Dictionary<EnumMemberDeclarationSyntax, int>();
            private EnumDeclarationSyntax _enum;
            private int _lastEnumMemberValue = -1;

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
                return node.TryGetLiteralValueFor<int>(out var value) 
                    ? value 
                    : throw new InvalidOperationException($"Invalid literal type: {node}");
            }

            public override int VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                var leftValue = node.Left.Accept(this);
                var rightValue = node.Right.Accept(this);

                switch (node.OperatorToken.Kind())
                {
                    case SyntaxKind.PlusToken:
                        return leftValue + rightValue;
                    case SyntaxKind.MinusToken:
                        return leftValue - rightValue;
                    case SyntaxKind.AsteriskToken:
                        return leftValue * rightValue;
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
