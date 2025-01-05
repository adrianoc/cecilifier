using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;

namespace Cecilifier.Core.AST.MemberDependencies;

internal record MemberDependency : IMemberDependencyFactory<MemberDependency>
{
    public CSharpSyntaxNode Declaration { get; set; }

    public void AddReference(MemberDependency dependency)
    {
        _dependencies.Add(dependency);
    }
    
    public static MemberDependency CreateInstance(CSharpSyntaxNode node) => new() { Declaration = node };
    public IReadOnlyList<MemberDependency> Dependencies => _dependencies;

    public void Accept(IMemberDependencyVisitor<MemberDependency> visitor)
    {
        if (!visitor.VisitMemberStart(this))
            return;
        
        foreach(var dependency in _dependencies)
            visitor.VisitDependency(dependency);
        visitor.VisitMemberEnd(this);
    }

    private readonly List<MemberDependency> _dependencies = new();
}
