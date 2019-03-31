using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    internal interface IAssemblyDiffVisitor
    {
        bool VisitModules(AssemblyDefinition source, AssemblyDefinition target);
        ITypeDiffVisitor VisitType(TypeDefinition sourceType);
    }
}
