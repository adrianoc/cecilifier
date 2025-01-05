using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;

namespace Cecilifier.Core.AST.MemberDependencies;

internal class ForwardMemberReferenceAvoidanceVisitor : IMemberDependencyVisitor<MemberDependency>
{
    private readonly CSharpSyntaxVisitor _syntaxVisitor;
    private readonly HashSet<MemberDependency> _seem = new();
 
    public ForwardMemberReferenceAvoidanceVisitor(CSharpSyntaxVisitor syntaxVisitor)
    {
        _syntaxVisitor = syntaxVisitor;
    }

    public bool VisitMemberStart(MemberDependency member)
    {
        if (!_seem.Add(member))
            return false;
        return true;
    }

    public void VisitMemberEnd(MemberDependency member)
    {
        member.Declaration.Accept(_syntaxVisitor);
    }

    public void VisitDependency(MemberDependency dependency)
    {
        if (_seem.Contains(dependency))
            return;
        dependency.Accept(this);
    }
}
