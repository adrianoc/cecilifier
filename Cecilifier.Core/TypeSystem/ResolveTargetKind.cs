namespace Cecilifier.Core.TypeSystem;

public enum ResolveTargetKind
{
    None,
    ComposedElementType, // `Composed Types` are types like arrays, pointer, references, etc. The element type of a composed type is the type being decorated. For instance,
                         //  for an `string[]` (an array of strings), the element type is `string`
                         // Any enum values equals to or smaller than `ComposeElementType` have special handling when resolving types.
        
    Field, 
    LocalVariable,
    Parameter,
    ReturnType,
    Instruction,
    TypeReference,
    AttributeNamedArgument,
    AttributeArgument,
    GenericTypeArgument,
    GenericTypeParameterConstraint
}
