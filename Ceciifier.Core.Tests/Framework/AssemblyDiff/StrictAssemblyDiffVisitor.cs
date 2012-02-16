using System.IO;
using Mono.Cecil;

namespace Ceciifier.Core.Tests.Framework.AssemblyDiff
{
	class StrictAssemblyDiffVisitor : IAssemblyDiffVisitor
	{
		public string Reason
		{
			get { return output.ToString(); }
		}

		public bool VisitModules(AssemblyDefinition sourceModule, AssemblyDefinition target)
		{
			return false;
		}

		public ITypeDiffVisitor VisitType(TypeDefinition sourceType)
		{
			return new StrictTypeDiffVisitor(output);
		}

		private TextWriter output = new StringWriter();
	}
}
