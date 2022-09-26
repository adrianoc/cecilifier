using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.TypeDependency;

public class TypeDependencyCollectorVisitor : CSharpSyntaxWalker
{
    private readonly CSharpCompilation compilation;
    private IDictionary<BaseTypeDeclarationSyntax, ISet<TypeSyntax>> dependencies = new Dictionary<BaseTypeDeclarationSyntax, ISet<TypeSyntax>>();
    private Stack<BaseTypeDeclarationSyntax> declaredTypes = new();
    private List<string> usings = new();
    
    public TypeDependencyCollectorVisitor(CSharpCompilation compilation)
    {
        this.compilation = compilation;
    }

    public IDictionary<BaseTypeDeclarationSyntax, ISet<TypeSyntax>> Dependencies => dependencies;
    public IReadOnlyList<string> Usings => usings;

    public override void VisitUsingDirective(UsingDirectiveSyntax node)
    {
        usings.Add(node.Name.ToString());
        base.VisitUsingDirective(node);
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        using (ProcessTypeDeclaration(node)) 
            base.VisitClassDeclaration(node);
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        using(ProcessTypeDeclaration(node))
            base.VisitInterfaceDeclaration(node);
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        using(ProcessTypeDeclaration(node))
            base.VisitEnumDeclaration(node);
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        using(ProcessTypeDeclaration(node))
            base.VisitStructDeclaration(node);
    }

    public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        AddCurrentTypeDependencyIfNotTheSame(node.Declaration.Type);
        base.VisitFieldDeclaration(node);
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        AddCurrentTypeDependencyIfNotTheSame(node.Type);
        base.VisitPropertyDeclaration(node);
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        AddCurrentTypeDependencyIfNotTheSame(node.ReturnType);
        base.VisitMethodDeclaration(node);
    }

    public override void VisitEventDeclaration(EventDeclarationSyntax node)
    {
        AddCurrentTypeDependencyIfNotTheSame(node.Type);
        base.VisitEventDeclaration(node);
    }

    public override void VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
    {
        AddCurrentTypeDependencyIfNotTheSame(node.Declaration.Type);
        base.VisitEventFieldDeclaration(node);
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        AddCurrentTypeDependencyIfNotTheSame(node.Type);
        base.VisitObjectCreationExpression(node);
    }
    
    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        AddCurrentTypeDependencyIfNotTheSame(node.Declaration.Type);
        base.VisitLocalDeclarationStatement(node);
    }

    public override void VisitParameter(ParameterSyntax node)
    {
        AddCurrentTypeDependencyIfNotTheSame(node.Type);
        base.VisitParameter(node);
    }

    public override void VisitCastExpression(CastExpressionSyntax node)
    {
        AddCurrentTypeDependencyIfNotTheSame(node.Type);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        if (!node.OperatorToken.IsKind(SyntaxKind.AsKeyword) && !node.OperatorToken.IsKind(SyntaxKind.IsKeyword))
            return;
        
        if (node.Right is TypeSyntax type)
            AddCurrentTypeDependencyIfNotTheSame(type);
        
        base.VisitBinaryExpression(node);
    }

    public override void VisitTypeConstraint(TypeConstraintSyntax node)
    {
        AddCurrentTypeDependencyIfNotTheSame(node.Type);
    }

    public override void VisitTypeArgumentList(TypeArgumentListSyntax node)
    {
        foreach (var type in node.Arguments)
        {
            AddCurrentTypeDependencyIfNotTheSame(type);
        }
    }

    public override void VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
    {
        AddCurrentTypeDependencyIfNotTheSame(node.Type.ElementType);
        base.VisitArrayCreationExpression(node);
    }

    public override void VisitArgument(ArgumentSyntax node)
    {
        if(node.Expression is TypeSyntax type)
            AddCurrentTypeDependencyIfNotTheSame(type);
    }

    public override void VisitTypeOfExpression(TypeOfExpressionSyntax node)
    {
        AddCurrentTypeDependencyIfNotTheSame(node.Type);
    }

    private void AddCurrentTypeDependencyIfNotTheSame(TypeSyntax type)
    {
        if (declaredTypes.Count == 0 || type == null) // type can be null for lambda parameters, for instance.
            return;
        
        var semanticModel = compilation.GetSemanticModel(type.SyntaxTree);
        if (String.Compare(declaredTypes.Peek().Identifier.Text, type.NameFrom(), StringComparison.Ordinal) != 0 && semanticModel.GetSymbolInfo(type).Symbol is ITypeSymbol { IsDefinition: true })
        {
            dependencies[declaredTypes.Peek()].Add(type);
        }
    }
    private DeclaredTypeTracker ProcessTypeDeclaration(BaseTypeDeclarationSyntax node)
    {
        var semanticModel = compilation.GetSemanticModel(node.SyntaxTree);

        var currentTypeDependencies = new HashSet<TypeSyntax>();
        dependencies.Add(node, currentTypeDependencies);

        var basesInSameCompilation = node.BaseList?.Types.Where(t => semanticModel.GetTypeInfo(t.Type).Type?.IsDefinition == true) ?? Array.Empty<BaseTypeSyntax>();
        foreach (var b in basesInSameCompilation)
            currentTypeDependencies.Add(b.Type);

        declaredTypes.Push(node);

        return new DeclaredTypeTracker(declaredTypes);
    }

    internal struct DeclaredTypeTracker : IDisposable
    {
        private Stack<BaseTypeDeclarationSyntax> toPop;

        public DeclaredTypeTracker(Stack<BaseTypeDeclarationSyntax> toPop) => this.toPop = toPop;

        public void Dispose()
        {
            if (toPop != null)
            {
                toPop.Pop();
                toPop = null;
            }
        }
    }
}
