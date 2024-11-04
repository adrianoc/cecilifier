using Cecilifier.Core.Mappings;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST;

internal class MemberDeclarationVisitor : SyntaxWalkerBase
{
    public MemberDeclarationVisitor(IVisitorContext context) : base(context) { }
        
    public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        using var _ = LineInformationTracker.Track(Context, node);
        new FieldDeclarationVisitor(Context).Visit(node);
    }

    public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
    {
        using var _ = LineInformationTracker.Track(Context, node);
        new PropertyDeclarationVisitor(Context).Visit(node);
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        using var _ = LineInformationTracker.Track(Context, node);
        new PropertyDeclarationVisitor(Context).Visit(node);
    }

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        using var _ = LineInformationTracker.Track(Context, node);
        new ConstructorDeclarationVisitor(Context).Visit(node);
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        new MethodDeclarationVisitor(Context).Visit(node);
    }

    public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
    {
        new ConversionOperatorDeclarationVisitor(Context).Visit(node);
    }

    public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node)
    {
        new ConversionOperatorDeclarationVisitor(Context).Visit(node);
    }

    public override void VisitEventDeclaration(EventDeclarationSyntax node)
    {
        new EventDeclarationVisitor(Context).Visit(node);
    }

    public override void VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
    {
        new EventDeclarationVisitor(Context).Visit(node);
    }
}
