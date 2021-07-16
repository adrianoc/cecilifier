namespace Cecilifier.Core.Naming
{
    // Keep this in sync with cecilifier.settings.js
    public enum ElementKind
    {
        Class,
        Struct,
        Interface,
        Enum ,
        Method,
        Delegate,
        Property,
        Field,
        Event,
        Constructor,
        StaticConstructor,
        LocalVariable,
        Parameter,
        MemberDeclaration, // Type/Member
        MemberReference, // Type/Member
        Label,
        Attribute,
        IL,
        GenericParameter,
        GenericInstance
    }
}
