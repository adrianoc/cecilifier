using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    public interface IMethodDiffVisitor : IMemberDiffVisitor
    {
        bool VisitReturnType(MethodDefinition source, MethodDefinition target);
        bool VisitAttributes(MethodDefinition source, MethodDefinition target);
        bool VisitBody(MethodDefinition source, MethodDefinition target);
        bool VisitBody(MethodDefinition source, MethodDefinition target, Instruction instruction);
        void VisitLocalVariables(MethodDefinition source, MethodDefinition target);
        void VisitDuplication(MethodDefinition method);
        bool VisitGenerics(MethodDefinition source, MethodDefinition target);
    }
}
