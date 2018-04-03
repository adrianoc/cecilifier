using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
	internal class StrictMethodDiffVisitor : IMethodDiffVisitor
	{
		private readonly TextWriter output;

		public StrictMethodDiffVisitor(TextWriter output)
		{
			this.output = output;
		}

		public bool VisitMissing(IMemberDefinition member, TypeDefinition target)
		{
			output.WriteLine(string.Format("[{0}] Method '{1}' not defined.", target.FullName, member));
			return false;
		}

		public bool VisitName(IMemberDefinition source, IMemberDefinition target)
		{
			output.WriteLine(string.Format("Method simple name ('{0}') matches, but not FQN. Expected {1} got {2}.", source.Name, source.FullName, target.FullName));
			return false;
		}

		public bool VisitDeclaringType(IMemberDefinition source, IMemberDefinition target)
		{
			output.WriteLine(string.Format("Declaring type differs in method: Expected '{0}' got {1}.", source.FullName, target.FullName));
			return false;
		}

		public bool VisitReturnType(MethodDefinition source, MethodDefinition target)
		{
			output.WriteLine(string.Format("[{0}] Method return types differs. Expected '{1}' got {2}.", target.FullName, source.ReturnType.FullName, target.ReturnType.FullName));
			return false;
		}

		public bool VisitAttributes(MethodDefinition source, MethodDefinition target)
		{
			output.WriteLine(string.Format("[{0}] Method attributes differs. Expected '{1}' got {2}.", target.FullName, source.Attributes, target.Attributes));
			return false;
		}

		public bool VisitBody(MethodDefinition source, MethodDefinition target)
		{
			output.WriteLine(string.Format("[{0}] Method body differs from {1}.", target.FullName, source.FullName));
			return false;
		}

		public bool VisitBody(MethodDefinition source, MethodDefinition target, Instruction instruction)
		{
			output.WriteLine(string.Format("[{0}] Method body differs from {1} at offset {2} (Instruction: {3}).", target.FullName, source.FullName, instruction.Offset, instruction));
			return false;
		}

		public void VisitLocalVariables(MethodDefinition source, MethodDefinition target)
		{
			output.WriteLine(string.Format("[{0}] Methods has different sets of local variables: {1} and {2}.", target.FullName, FormatLocalVariables(source.Body.Variables), FormatLocalVariables(target.Body.Variables)));
		}

		public void VisitDuplication(MethodDefinition method)
		{
			output.WriteLine(string.Format("Duplicated method found: {0}.", method.FullName));
		}

		private string FormatLocalVariables(Collection<VariableDefinition> variables)
		{
			//return variables.Aggregate("", (acc, curr) => acc + ", " + curr. + "(" + curr.VariableType.Name + ")");
            //TODO: VariableDefinition has no name????

		    return "?";
		}
	}
}