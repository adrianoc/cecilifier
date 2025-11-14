using Cecilifier.Core.AST;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.DelayedDefinitions;

/// <summary>
/// 
/// </summary>
/// <param name="TypeQualifiedName"></param>
/// <param name="TypeReferenceVariable">Name of the variable that stores the type reference emitted to represent a type definition. See <see cref="TypeDefinitionVariable"/></param>
internal record struct TypeDefinitionRecord(string TypeQualifiedName, string TypeReferenceVariable)
{
    
    /// <summary>
    /// Name of the variable that stores the type definition emitted to represent a type definition. This is only valid after the type has been processed. See <see cref="TypeReferenceVariable"/>
    /// </summary>
    public string TypeDefinitionVariable { get; internal set; }
    
    /// <summary>
    /// The name of the variable representing the definition of the first field of the type, or the first field of the following type
    /// or System.Reflection.Metadata.Ecma335.MetadataTokens.FieldDefinitionHandle(1) if no type in the module defines
    /// fields. See MetadataBuilder.AddMethodDefinition() for more details.
    /// </summary>
    public string? FirstFieldHandle { get; set; }

    /// <summary>
    /// The name of the variable representing the definition of the first method of the type. <see cref="FirstFieldHandle"/> 
    /// </summary>
    public string? FirstMethodHandle { get; set; }
    
    public IList<PropertyDefinitionRecord> Properties { get; } = new List<PropertyDefinitionRecord>();

    public required DelayedTypeDefinitionAction DefinitionFunction { internal get; init; }

    public IList<Action<IVisitorContext, string>> Attributes { get; }  = new List<Action<IVisitorContext, string>>();
    
    public IList<MethodDefinitionRecord> Methods { get; } = new List<MethodDefinitionRecord>();
}
