using System;

namespace Cecilifier.Core.Variables;

[Flags]
public enum VariableMemberKind
{
    Type = 1 << 1,
    TypeParameter = 1 << 2,
    Field = 1 << 3,
    Method = 1 << 4,
    Parameter = 1 << 5,
    LocalVariable = 1 << 6,
    ModuleReference = 1 << 7,
}
