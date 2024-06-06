namespace Cecilifier.Core;

public struct Constants
{
    public const string AssemblyReferenceCacheBasePath = "/tmp/CecilifierUserAssemblyReferenceCache";

    public struct Cecil
    {
        public const string StaticFieldAttributes = "FieldAttributes.Public | FieldAttributes.Static";
        public const string StaticTypeAttributes = "TypeAttributes.Abstract | TypeAttributes.Sealed";
        public const string StaticClassAttributes = $"TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | {StaticTypeAttributes}";
        public const string InterfaceMethodDefinitionAttributes = "MethodAttributes.NewSlot | MethodAttributes.Virtual"; // Some common method attributes (like HideBySig) will be explicitly added.
        public const string MethodAttributesSpecialName = "MethodAttributes.SpecialName";
        public const string MethodAttributesStatic = "MethodAttributes.Static";
        public const string HideBySigVirtual = "MethodAttributes.HideBySig | MethodAttributes.Virtual";
        public const string HideBySigNewSlotVirtual = "MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual";
        public const string DelegateMethodAttributes = $"MethodAttributes.Public | {HideBySigNewSlotVirtual}";
        public const string PublicOverrideMethodAttributes = $"MethodAttributes.Public | {HideBySigVirtual}";
        public const string PublicOverrideOperatorAttributes = $"MethodAttributes.Public | MethodAttributes.HideBySig | {MethodAttributesSpecialName} | {MethodAttributesStatic}";
        public const string PublicInstanceMethod = $"MethodAttributes.Public | MethodAttributes.HideBySig";

        public const string CtorAttributes = "MethodAttributes.RTSpecialName | MethodAttributes.SpecialName";
        public const string InstanceConstructorName = "ctor";
        public const string StaticConstructorName = "cctor";
    }

    public struct ParameterAttributes
    {
        public const string None = "ParameterAttributes.None";
        public const string In = "ParameterAttributes.In";
        public const string Out = "ParameterAttributes.Out";
        public const string Optional = "ParameterAttributes.Optional";
    }

    public struct ContextFlags
    {
        /// <summary>
        /// In expressions such as `Index i; i = ^10` the assignment is actually a call to the Index constructor which in IL
        /// should be translated to a `load address of the value type` followed by a call to the constructor (instead of the
        /// normal store local, field, parameter). This will take place when we are visiting the lhs of the expression, so
        /// we turn on a flag to indicate that the address of the lhs should be loaded  
        /// </summary>
        public const string PseudoAssignmentToIndex = "pseudo-assignment-to-index";
        public const string HasStackallocArguments = "has-stackalloc-argument";
        public const string RefReturn = "ref-return";
        public const string Fixed = "fixed";
        public const string InRangeExpression = "in-range-expressions";
        public const string MemberReferenceRequiresConstraint = "member-reference-requires-constraint";
        public const string DefaultMemberTracker = "default-member-tracker"; // used to ensure only one DefaultMemberAttribute is added to a type even if it happens to have multiple indexers.
    }

    public struct CompilerGeneratedTypes
    {
        public const string PrivateImplementationDetails = "<PrivateImplementationDetails>";
        public const string PrivateImplementationDetailsModifiers = "TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.AnsiClass | TypeAttributes.AutoLayout";
        
        /// <summary>modifiers/name for compiler emitted type with field holding the data used to optimize array/stackalloc initialization</summary>
        public static string StaticArrayInitTypeNameFor(long size) =>  $"__StaticArrayInitTypeSize={size}";
        public const string StaticArrayRawDataHolderTypeModifiers = "TypeAttributes.NestedAssembly | TypeAttributes.Sealed | TypeAttributes.AnsiClass | TypeAttributes.ExplicitLayout";
        /// <summary>modifiers for compiler emitted field holding the data used to optimize array/stackalloc initialization</summary>
        public const string StaticArrayInitFieldModifiers = "FieldAttributes.Assembly | FieldAttributes.Static | FieldAttributes.InitOnly";
    }

    public struct Common
    {
        public const string RuntimeHelpersInitializeArrayMethodName = "InitializeArray";
        public const string RuntimeConfigJsonExt = ".runtimeconfig.json";
    }

    public struct FrontEnd
    {
        public const string PathNotFoundRedirectQueryParameter = "redirectedFrom";
    }
}

