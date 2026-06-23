using System;

namespace Cecilifier.Core.AST;

/// <summary>
/// Used for methods without a body (extern, most interface members, etc)
/// </summary>
/// <remarks>Basically it is used to carry around the name of the variable storing the method definition.</remarks>
/// <param name="relatedMethodVar"></param>
public class EmptyBodyIlContext(string relatedMethodVar) : IlContext(string.Empty, relatedMethodVar)
{
    public override void Materialize() => throw new Exception();
    public override string VariableName => throw new Exception();
}
