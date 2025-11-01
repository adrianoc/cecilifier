namespace Cecilifier.Core.TypeSystem;

public enum ResolveTargetKind
{
    None,
    ArrayElementType, // Any enum values equals to or smaller than `ArrayElementType` have special handling when resolving types.
        
    Field, 
    LocalVariable,
    Parameter,
    ReturnType,
}
