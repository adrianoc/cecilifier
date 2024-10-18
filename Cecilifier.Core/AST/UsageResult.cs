using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.AST;

struct UsageResult
{
    public UsageKind Kind { get; }
    public ISymbol Target { get; }
    public static UsageResult None = new(UsageKind.None, null); 

    public static implicit operator UsageKind(UsageResult result) => result.Kind;

    internal UsageResult(UsageKind kind, ISymbol target) => (Kind, Target) = (kind, target);
}
