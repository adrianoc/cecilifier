using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.CodeGeneration;

internal partial class PrivateImplementationDetailsGenerator
{
    static IMethodSymbol GetUnsafeAsMethod(IVisitorContext context)
    {
        var candidates = context.RoslynTypeSystem.SystemRuntimeCompilerServicesUnsafe
            .GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.Name == "As" && m.Parameters.Length == 1 && m.Parameters[0].RefKind == RefKind.Ref);

        VerifyOnlyOneMatch(candidates);
        return candidates.Single();
    }
    
    static IMethodSymbol GetUnsafeAddMethod(IVisitorContext context)
    {
        var candidates = context.RoslynTypeSystem.SystemRuntimeCompilerServicesUnsafe
            .GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.Name == "Add" && m.Parameters.Length == 2 && m.Parameters[0].RefKind == RefKind.Ref && m.Parameters[1].Type.Name == "Int32");

        VerifyOnlyOneMatch(candidates);
        return candidates.Single();
    }

    [Conditional("DEBUG")]
    private static void VerifyOnlyOneMatch(IEnumerable<IMethodSymbol> candidates)
    {
        Debug.Assert(candidates.Count() == 1);
    }
}
