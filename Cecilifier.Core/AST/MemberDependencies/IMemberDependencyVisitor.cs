namespace Cecilifier.Core.AST.MemberDependencies;

internal interface IMemberDependencyVisitor<T> where T : MemberDependency
{
    bool VisitMemberStart(T member);
    void VisitMemberEnd(T member);
    void VisitDependency(T dependency);
}
