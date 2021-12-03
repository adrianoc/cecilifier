namespace Cecilifier.Core;

public struct Constants
{
    public static ContextFlags ContextFlags = new();
}

public struct ContextFlags
{
    public readonly string RefReturn = "ref-return";
    public readonly string Fixed = "fixed";
}

