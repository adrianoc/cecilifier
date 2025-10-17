#nullable enable
using System;

namespace Cecilifier.Core.ApiDriver;

[Flags]
public enum MemberOptions
{
    None = 0,
    Static = 0x1 << 0,
    InitOnly =  0x1 << 1,
}
