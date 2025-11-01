using System;

namespace Cecilifier.Core.TypeSystem;

/// <summary>
/// Represents a resolved type
/// </summary>
/// <remarks>
/// A resolved type can be either the name of a variable in the generated code that stores an expression returned by one of the methods of <see cref="ITypeResolver"/>
/// or the expression itself.
/// </remarks>
public readonly record struct ResolvedType
{
    private readonly string _resolved;
    private readonly object _details;

    public ResolvedType(string resolved) => (_details, _resolved) = (null, resolved);
    private ResolvedType(object details) => _details = details;

    public static ResolvedType FromDetails<TDetails>(TDetails details) where TDetails : struct => new(details); 
    public TDetails GetDetails<TDetails>() => _details == null 
                                                    ? throw new NullReferenceException() 
                                                    : (TDetails)_details;

    public string Expression
    {
        get
        {
            if (_details != null)
            {
                return _details.ToString();
            }

            return _resolved;
        }
    }

    public static implicit operator ResolvedType(string typeName) => new(typeName);
    public static bool operator true(in ResolvedType rt) => rt.Expression != null;
    public static bool operator false(ResolvedType rt) => rt.Expression == null;
    
    public override string ToString() => Expression;
}
