using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.TypeSystem;

public interface IMethodResolver
{
    // Returns an expression? that represents the resolved method
    string Resolve(IMethodSymbol method, IVisitorContext context);
}
