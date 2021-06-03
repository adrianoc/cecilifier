using System;

namespace Cecilifier.Core.Naming
{
    [Flags]
    public enum NamingOptions
    {
        DifferentiateDeclarationsAndReferences = 0x1,
        PrefixInstructionsWithILOpCodeName = 0x2,
        /// <summary>Append type/member name to the variables used to store Mono.Cecil objects representing them.</summary>
        AppendElementNameToVariables = 0x4,
        PrefixVariableNamesWithElementKind = 0x8,
        SuffixVariableNamesWithUniqueId = 0x10,
        SeparateCompoundWords = 0x20,
        CamelCaseElementNames = 0x40,
        AddCommentsToMemberDeclarations = 0x80,
        IncludeSourceInErrorReports = 0x100,

        All =  DifferentiateDeclarationsAndReferences
               | PrefixInstructionsWithILOpCodeName
               | AppendElementNameToVariables
               | PrefixVariableNamesWithElementKind
               | SuffixVariableNamesWithUniqueId
               | SeparateCompoundWords
               | CamelCaseElementNames
               | AddCommentsToMemberDeclarations |
               IncludeSourceInErrorReports
    }
}
