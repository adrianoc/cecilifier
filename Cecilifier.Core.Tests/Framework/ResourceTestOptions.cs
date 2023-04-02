using System.IO;
using Cecilifier.Core.Tests.Framework.AssemblyDiff;

namespace Cecilifier.Core.Tests.Framework;

public ref struct ResourceTestOptions
{
    public ResourceTestOptions()
    {
        BuildType = BuildType.Dll;
        AssemblyComparison = new StrictAssemblyDiffVisitor();
    }

    internal string ResourceName { get; init; }
    internal IAssemblyDiffVisitor AssemblyComparison { get; init; }
    internal Stream ToBeCecilified { get; init; }
    internal BuildType BuildType { get; init; }
}
