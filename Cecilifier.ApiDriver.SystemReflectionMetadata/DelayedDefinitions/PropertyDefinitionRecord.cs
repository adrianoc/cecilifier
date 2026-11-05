using Cecilifier.Core.AST;
using Cecilifier.Core.TypeSystem;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;

internal record struct PropertyDefinitionRecord(string Name, string DefinitionVariable, string DeclaringTypeName, List<ParameterSpec> Parameters, ResolvedType Type, Action<IVisitorContext, string, string, string, List<ParameterSpec>, ResolvedType> Processor)
{
    public bool IsValid => Name != null;
}
