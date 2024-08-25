namespace Cecilifier.Core.AST;

public enum MethodDispatchInformation
{
    MostLikelyVirtual, // Virtual/Non-virtual dispatching depends on other factors
    NonVirtual, // Method should be called non virtually
    MostLikelyNonVirtual // Most likely a non-virtual call but it depends on other factors
}
