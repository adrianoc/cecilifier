using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    internal partial class StatementVisitor
    {
        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            ExpressionVisitor.Visit(Context, _ilVar, node.Expression);

            var forEachTargetType = Context.GetTypeInfo(node.Expression).Type.EnsureNotNull();

            var getEnumeratorMethod = forEachTargetType.GetMembers("GetEnumerator").OfType<IMethodSymbol>().Single();
            var enumeratorType = EnumeratorTypeFor(getEnumeratorMethod);
            var moveNextMethod = enumeratorType.GetMembers("MoveNext").Single().EnsureNotNull<ISymbol, IMethodSymbol>();
            var currentGetter = getEnumeratorMethod.ReturnType.GetMembers("get_Current").Single().EnsureNotNull<ISymbol, IMethodSymbol>();
            
            // Adds a variable to store current value in the foreach loop.
            Context.WriteNewLine();
            Context.WriteComment("variable to store current value in the foreach loop.");
            var foreachCurrentValueVarName = CodeGenerationHelpers.AddLocalVariableToCurrentMethod(Context, node.Identifier.ValueText, Context.TypeResolver.Resolve(currentGetter.GetMemberType())).VariableName;
            
            // Get the enumerator..
            Context.WriteNewLine();
            Context.WriteComment("variable to store the returned 'IEnumerator<T>'.");
            AddMethodCall(_ilVar, getEnumeratorMethod);
            var enumeratorVariableName = CodeGenerationHelpers.StoreTopOfStackInLocalVariable(Context, _ilVar, "enumerator", getEnumeratorMethod.ReturnType).VariableName;
            
            var endOfLoopLabelVar = Context.Naming.Label("endForEach");
            CreateCilInstruction(_ilVar, endOfLoopLabelVar, OpCodes.Nop);

            // loop while enumerable.MoveNext() == true
            
            var forEachLoopBegin = AddCilInstructionWithLocalVariable(_ilVar, OpCodes.Nop);
            
            Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, enumeratorVariableName);
            AddMethodCall(_ilVar, moveNextMethod);
            Context.EmitCilInstruction(_ilVar, OpCodes.Brfalse, endOfLoopLabelVar);
            
            Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, enumeratorVariableName);
            AddMethodCall(_ilVar, currentGetter);
            Context.EmitCilInstruction(_ilVar, OpCodes.Stloc, foreachCurrentValueVarName);
            
            // process body of foreach
            Context.WriteNewLine();
            Context.WriteComment("foreach body");
            node.Statement.Accept(this);
            Context.WriteComment("end of foreach body");
            Context.WriteNewLine();
            
            Context.EmitCilInstruction(_ilVar, OpCodes.Br, forEachLoopBegin);
            Context.WriteNewLine();
            Context.WriteComment("end of foreach loop");
            Context.WriteCecilExpression($"{_ilVar}.Append({endOfLoopLabelVar});");
            Context.WriteNewLine();
        }

        public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
        {
            base.VisitForEachVariableStatement(node);
        }

        /*
         * either the type returned by GetEnumerator() implements `IEnumerator` interface *or*
         * it abides to the enumerator patterns, i.e, it has the following members:
         * 1. public bool MoveNext() method
         * 2. public T Current property ('T' can be any type)
         */
        private ITypeSymbol EnumeratorTypeFor(IMethodSymbol getEnumeratorMethod)
        {
            var enumeratorType = getEnumeratorMethod.ReturnType.Interfaces.SingleOrDefault(itf => itf.Name == "IEnumerator");
            return enumeratorType ?? getEnumeratorMethod.ReturnType;
        }
    }
}
