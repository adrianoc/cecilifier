using System.IO;
using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    internal class StrictAssemblyDiffVisitor : IAssemblyDiffVisitor
    {
        private readonly TextWriter output = new StringWriter();

        public string Reason => output.ToString();

        public bool VisitModules(AssemblyDefinition sourceModule, AssemblyDefinition target)
        {
            return false;
        }

        public ITypeDiffVisitor VisitType(TypeDefinition sourceType)
        {
            return new StrictTypeDiffVisitor(output);
        }
    }
}
