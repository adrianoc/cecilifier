using Cecilifier.Core.AST;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;

public record struct EventDefinitionRecord(string Name, string DefinitionVariable, string DeclaringTypeName, Action<IVisitorContext, string, string, string> Processor)
{
    public bool IsValid => Name != null;
}

