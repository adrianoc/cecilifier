using System;
using System.Diagnostics.CodeAnalysis;
using Cecilifier.Core.Variables;

namespace Cecilifier.Core.Extensions;

public static class DefinitionVariableExtensions
{
    [ExcludeFromCodeCoverage]
    public static void ThrowIfVariableIsNotValid(this DefinitionVariable variable)
    {
        if (!variable.IsValid)
            throw new Exception($"Could not resolve variable definition for {variable.MemberName} ({variable.Kind}, Parent: {variable.ParentName ?? "N/A"})");
    }
}
