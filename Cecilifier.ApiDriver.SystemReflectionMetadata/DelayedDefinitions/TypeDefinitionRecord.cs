namespace Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;

/// <summary>
/// 
/// </summary>
/// <param name="TypeQualifiedName"></param>
/// <param name="TypeVarName"></param>
internal record struct TypeDefinitionRecord(string TypeQualifiedName, string TypeVarName)
{
    /// <summary>
    /// The name of the variable representing the definition of the first field of the type, or the first field of the following type
    /// or System.Reflection.Metadata.Ecma335.MetadataTokens.FieldDefinitionHandle(1) if no types in the module defines
    /// fields. See MetadataBuilder.AddMethodDefinition() for more details.
    /// </summary>
    public string FirstFieldHandle { get; set; }
    public string FirstMethodHandle { get; set; }
    
    /// <summary>
    /// 
    /// </summary>
    public required Action<SystemReflectionMetadataContext, TypeDefinitionRecord> DefinitionFunction { internal get; init; }
}
