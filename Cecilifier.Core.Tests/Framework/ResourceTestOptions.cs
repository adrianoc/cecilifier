using System.IO;
using Cecilifier.Core.Tests.Framework.AssemblyDiff;

namespace Cecilifier.Core.Tests.Framework;

public ref struct ResourceTestOptions
{
    public ResourceTestOptions()
    {
        BuildType = BuildType.Dll;
        AssemblyComparison = new StrictAssemblyDiffVisitor();
        FailOnAssemblyVerificationErrors = true;
    }

    internal string ResourceName { get; init; }
    internal IAssemblyDiffVisitor AssemblyComparison { get; init; }
    internal Stream ToBeCecilified { get; set; }
    internal BuildType BuildType { get; init; }
    internal bool FailOnAssemblyVerificationErrors { get; init; }
    internal string IgnoredILErrors { get; set; }
}
