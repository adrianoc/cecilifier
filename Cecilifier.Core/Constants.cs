namespace Cecilifier.Core;

public struct Constants
{
    public const string AssemblyReferenceCacheBasePath = "/tmp/CecilifierUserAssemblyReferenceCache";

    public struct Cecil
    {
        public const string StaticFieldAttributes = "FieldAttributes.Public | FieldAttributes.Static";
        public const string StaticTypeAttributes = "TypeAttributes.Abstract | TypeAttributes.Sealed";
        public const string StaticClassAttributes = $"TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | {StaticTypeAttributes}";
        public const string InterfaceMethodAttributes = "MethodAttributes.SpecialName | MethodAttributes.Public | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.Abstract | MethodAttributes.HideBySig";
        public const string MethodAttributesSpecialName = "MethodAttributes.SpecialName";
        public const string DelegateMethodAttributes = "MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual";
        
        public const string CtorAttributes = "MethodAttributes.RTSpecialName | MethodAttributes.SpecialName";
        public const string InstanceConstructorName = "ctor";
        public const string StaticConstructorName = "cctor";


        public Cecil() { }
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
    }
}

