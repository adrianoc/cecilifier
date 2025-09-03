namespace Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;

internal record MethodDefinitionRecord(Func<SystemReflectionMetadataContext, MethodDefinitionRecord, string> DefinitionFunction, string DeclaringTypeVarName)
{
    public string FirstParameterHandle { get; init; } = "MetadataTokens.ParameterHandle(1)";
}
