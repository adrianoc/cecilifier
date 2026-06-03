#nullable enable
using System;

namespace Cecilifier.Core.TypeSystem;

[Flags]
public enum TypeResolutionOptions
{
    None = 0x0,
    IsByRef = 0x1 << 0,
    IsValueType = 0x1 << 1,
    
    /// <summary>Register the final local variable emitted to store the newly resolved type. This avoids code bloating.</summary>
    RegisterVariables = 0x1 << 2, 
}
