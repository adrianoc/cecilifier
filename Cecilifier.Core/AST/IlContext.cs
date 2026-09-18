using System.Diagnostics;

namespace Cecilifier.Core.AST;

[DebuggerDisplay("{VariableName} (method: {AssociatedMethodVariable})")]
public class IlContext
{
    protected IlContext(string variableName, string relatedMethodVar) => (VariableName, AssociatedMethodVariable) = (variableName, relatedMethodVar);
    public static readonly IlContext None = new(string.Empty, string.Empty);

    /// <summary>Ensures that any code that needs to be generated before the variable is used is generated. </summary>
    /// <remarks>
    /// Note that accessing <seealso cref="VariableName"/> has the same effect. This method is intended to be used in places
    /// where even though no such access exists, materialization of the variable is still needed (in general due to
    /// ordering of statements).
    /// </remarks>
    public virtual void Materialize() { }
    
    public virtual string VariableName { get; }
    public string AssociatedMethodVariable { get; }
}
