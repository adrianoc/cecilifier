using System;
using System.IO;
using Cecilifier.Core.Tests.Framework.AssemblyDiff;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.Tests.Framework;


public struct IgnoredKnownIssue
{
    public static IgnoredKnownIssue MiscILVerifyVailuresNeedsInvestigation = new IgnoredKnownIssue("https://github.com/adrianoc/cecilifier/issues/227");

    private IgnoredKnownIssue(string issueURL, bool failTests = false) => _failTests = failTests;
    private readonly bool _failTests;

    public static implicit operator bool(IgnoredKnownIssue s) => s._failTests;
}
public ref struct CecilifyTestOptions
{
    public CecilifyTestOptions()
    {
        BuildType = BuildType.Dll;
        AssemblyComparison = new StrictAssemblyDiffVisitor();
        FailOnAssemblyVerificationErrors = true;
    }

    internal string ResourceName { get; init; }
    internal IAssemblyDiffVisitor AssemblyComparison { get; init; }
    internal Func<Instruction, Instruction, bool?> InstructionComparer { get; init; }
    internal Stream ToBeCecilified { get; set; }
    internal BuildType BuildType { get; init; }
    internal bool FailOnAssemblyVerificationErrors { get; init; }
    internal string IgnoredILErrors { get; set; }
}
