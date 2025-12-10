#nullable enable
using System;

namespace Cecilifier.Core.TypeSystem;

[Flags]
public enum TypeResolutionOptions
{
    None = 0x0,
    IsByRef = 0x1 << 0,
    IsValueType = 0x1 << 1,
}
