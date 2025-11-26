namespace Cecilifier.ApiDriver.SystemReflectionMetadata;

internal static class ApiDriverConstants
{
    internal const string MethodDefinitionTableNextAvailableEntry = "MetadataTokens.MethodDefinitionHandle(metadata.GetRowCount(TableIndex.MethodDef) + 1)";
    internal const string FieldDefinitionTableNextAvailableEntry = "MetadataTokens.FieldDefinitionHandle(metadata.GetRowCount(TableIndex.Field) + 1)";
}
