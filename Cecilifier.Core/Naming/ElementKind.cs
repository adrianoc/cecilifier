namespace Cecilifier.Core.Naming
{
    // Keep this in sync with cecilifier.settings.js
    public enum ElementKind
    {
        Class,
        Struct,
        Record,
        Interface,
        Enum,
        Method,
        Delegate,
        Property,
        Field,
        Event,
        Constructor,
        StaticConstructor,
        LocalVariable,
        Parameter,
        MemberReference, // Type/Member
        Label,
        Attribute,
        IL,
        GenericParameter,
        GenericInstance,
        None
    }
}
