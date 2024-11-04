using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST.MemberDependencies;

/// <summary>
/// Given a <see cref="TypeDeclarationSyntax"/> computes a list of dependencies (of its own members) for each of its members.
/// This is used to allow Cecilifier to process a type's members in an order that minimizes interleaving code generated for
/// its members. 
/// </summary>
internal class MemberDependencyCollector<T> where T : MemberDependency, IMemberDependencyFactory<MemberDependency>
{
    public IReadOnlyCollection<MemberDependency> Process(TypeDeclarationSyntax node, SemanticModel semanticModel)
    {
        var collectorVisitor = new MemberCollectorVisitor<T>(semanticModel, node);
        node.Accept(collectorVisitor);
        
        return collectorVisitor.Dependencies;
    }

    private class MemberCollectorVisitor<TNode> : CSharpSyntaxWalker where TNode : MemberDependency, IMemberDependencyFactory<MemberDependency>
    {
        public IReadOnlyCollection<TNode> Dependencies => _members.Values;

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (node == _targetType)
                base.VisitClassDeclaration(node);
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            if (node == _targetType)
                base.VisitStructDeclaration(node);
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            if (node == _targetType)
                base.VisitRecordDeclaration(node);
        }
        
        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            ProcessMemberDeclarationNode(node, n => base.VisitMethodDeclaration(n));
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            foreach (var variable in node.Declaration.Variables)
                TryGetMemberForDeclaration(variable, out _);

            base.VisitFieldDeclaration(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            ProcessMemberDeclarationNode(node, n => base.VisitPropertyDeclaration(n));
        }
            
        public override void VisitEventDeclaration(EventDeclarationSyntax node)
        {
            ProcessMemberDeclarationNode(node, n => base.VisitEventDeclaration(n));
        }

        public override void VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
        {
            foreach (var evt in node.Declaration.Variables)
                TryGetMemberForDeclaration(evt, out _);
                
            base.VisitEventFieldDeclaration(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            ProcessMemberDeclarationNode(node, n => base.VisitConstructorDeclaration(n));
        }

        public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
        {
            ProcessMemberDeclarationNode(node, localNode => base.VisitIndexerDeclaration(localNode));
        }

        public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node)
        {
            ProcessMemberDeclarationNode(node, localNode => base.VisitOperatorDeclaration(localNode));
        }

        public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
        {
            ProcessMemberDeclarationNode(node, localNode => base.VisitConversionOperatorDeclaration(localNode));
        }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            ProcessMemberDeclarationNode(node, localNode => base.VisitLocalFunctionStatement(localNode));
        }

        private void ProcessMemberDeclarationNode<T>(T node, Action<T> action) where T : CSharpSyntaxNode
        {
            if (!TryGetMemberForDeclaration(node, out var member))
                action(node);
                
            _current.Push(member);
            action(node);
            _current.Pop();
        }

        #region Collect member references

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (_current.Count > 0 && TryGetMemberForUsage(node, out var referencee))
            {
                _current.Peek().AddReference(referencee);
            }
            base.VisitIdentifierName(node);
        }

        public override void VisitConstructorInitializer(ConstructorInitializerSyntax node)
        {
            if (node.IsKind(SyntaxKind.ThisConstructorInitializer))
            {
                if (_current.Count > 0 && TryGetMemberForUsage(node, out var referencee))
                {
                    _current.Peek().AddReference(referencee);
                }
            }
            base.VisitConstructorInitializer(node);
        }

        public override void VisitThisExpression(ThisExpressionSyntax node)
        {
            if (_current.Count > 0 && TryGetMemberForUsage((CSharpSyntaxNode)node.Parent, out var referencee))
            {
                _current.Peek().AddReference(referencee);
            }
            base.VisitThisExpression(node);
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (_current.Count > 0 && TryGetMemberForUsage(node, out var referencee))
            {
                _current.Peek().AddReference(referencee);
            }
            base.VisitBinaryExpression(node);
        }

        public override void VisitCastExpression(CastExpressionSyntax node)
        {
            if (_current.Count > 0 && TryGetMemberForUsage(node, out var referencee))
            {
                _current.Peek().AddReference(referencee);
            }
            base.VisitCastExpression(node);
        }
        
        private bool TryGetMemberForUsage(CSharpSyntaxNode candidateNode, out TNode referencee)
        {
            referencee = null;
            var symbol = _semanticModel.GetSymbolInfo(candidateNode).Symbol;
            if (symbol == null)
                return false;

            if (_current.Count > 0)
            {
                var currentSymbol = _semanticModel.GetDeclaredSymbol(_current.Peek().Declaration);
                if (!SymbolEqualityComparer.Default.Equals(currentSymbol.ContainingType, symbol.ContainingType)
                    && !SymbolEqualityComparer.Default.Equals(currentSymbol.ContainingType, symbol))
                    return false;
            }

            return TryGetMemberOfCurrentTypeUsage(symbol, null, out referencee);
        }
            
        private bool TryGetMemberForDeclaration(CSharpSyntaxNode memberDeclaration, out TNode referencee)
        {
            var symbol = _semanticModel.GetDeclaredSymbol(memberDeclaration);
            if (symbol == null)
            {
                referencee = null;
                return false;
            }

            // for fields/events we pass its `VariableDeclarator` as the *memberDeclaration* in order to be able to retrieve the correct ISymbol
            // above. However, the rest of the code expects to get either a FieldDeclarationSyntax or a EventDeclarationSyntax, so we grab those
            // through the parent chain.
            var declarationNodeAlias = memberDeclaration.IsKind(SyntaxKind.VariableDeclarator) &&
                                           (memberDeclaration.Parent!.Parent.IsKind(SyntaxKind.FieldDeclaration) || memberDeclaration.Parent.Parent.IsKind(SyntaxKind.EventFieldDeclaration))
                                           ? (CSharpSyntaxNode) memberDeclaration.Parent.Parent
                                           : memberDeclaration;
            
            var found = TryGetMemberOfCurrentTypeUsage(symbol, declarationNodeAlias, out referencee);
            if (found && referencee.Declaration == null)
                referencee.Declaration = declarationNodeAlias;
                
            return found;
        }
            
        private bool TryGetMemberOfCurrentTypeUsage(ISymbol symbol, CSharpSyntaxNode? memberDeclaration, out TNode member)
        {
            ArgumentNullException.ThrowIfNull(symbol);

            var hashCode = HashCode.Combine(symbol.OriginalDefinition.ToDisplayString(), symbol.OriginalDefinition.Kind, symbol.IsStatic);
            if (_members.TryGetValue(hashCode, out member))
            {
                return true;
            }

            var shouldAdd = symbol.Kind == SymbolKind.Method
                            || symbol.Kind == SymbolKind.Property
                            || symbol.Kind == SymbolKind.Event
                            || symbol.Kind == SymbolKind.Field
                            || symbol is IParameterSymbol { IsThis: true }; // ctor():this() {}
                            
            if (shouldAdd)
            {
                member = (TNode) TNode.CreateInstance(memberDeclaration);
                _members.Add(hashCode, member);
            }

            return shouldAdd;
        }

        #endregion

        private readonly Dictionary<int, TNode> _members = new();
        private readonly Stack<TNode> _current = new();
        private readonly SemanticModel _semanticModel;
        private readonly TypeDeclarationSyntax _targetType;

        public MemberCollectorVisitor(SemanticModel semanticModel, TypeDeclarationSyntax targetType)
        {
            _semanticModel = semanticModel;
            _targetType = targetType;
        }
    }
}
