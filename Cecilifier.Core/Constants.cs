namespace Cecilifier.Core;

public struct Constants
{
    public static ContextFlagsValues ContextFlags = new();
    public static ParameterAttributesValues ParameterAttributes = new();
    
    public struct ParameterAttributesValues
    {
        public readonly string None = "ParameterAttributes.None";
        public readonly string In = "ParameterAttributes.In";
        public readonly string Out = "ParameterAttributes.Out";
        public readonly string Optional = "ParameterAttributes.Optional";

        public ParameterAttributesValues() { }
    }
    
    public struct ContextFlagsValues
    {
        /// <summary>
        /// In expressions such as `Index i; i = ^10` the assignment is actually a call to the Index constructor which in IL
        /// should be translated to a `load address of the value type` followed by a call to the constructor (instead of the
        /// normal store local, field, parameter). This will take place when we are visiting the lhs of the expression, so
        /// we turn on a flag to indicate that the address of the lhs should be loaded  
        /// </summary>
        public readonly string PseudoAssignmentToIndex = "pseudo-assignment-to-index";
        public readonly string HasStackallocArguments = "has-stackalloc-argument";
        public readonly string RefReturn = "ref-return";
        public readonly string Fixed = "fixed";
        public readonly string InRangeExpression = "in-range-expressions";

        public ContextFlagsValues() { }
    }
}

