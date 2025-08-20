using System.Collections.Generic;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.Naming;

namespace Cecilifier.Core;

public record CecilifierOptions
{
    public INameStrategy Naming { get; init; } = new DefaultNameStrategy();

    public IReadOnlyList<string> References { get; init; }
}
