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

            var  enumerableType = Context.GetTypeInfo(node.Expression).Type.EnsureNotNull();
            var getEnumeratorMethod = GetEnumeratorMethodFor(enumerableType);
            var enumeratorType = EnumeratorTypeFor(getEnumeratorMethod);
            
            var enumeratorMoveNextMethod = MoveNextMethodFor(enumeratorType);
            var enumeratorCurrentMethod = CurrentMethodFor(enumeratorType);
            
            ProcessForEach(node, getEnumeratorMethod, enumeratorCurrentMethod, enumeratorMoveNextMethod);
        }

        private void ProcessForEach(ForEachStatementSyntax node, IMethodSymbol getEnumeratorMethod, IMethodSymbol enumeratorCurrentMethod, IMethodSymbol enumeratorMoveNextMethod)
        {
            // Adds a variable to store current value in the foreach loop.
            Context.WriteNewLine();
            Context.WriteComment("variable to store current value in the foreach loop.");
            var foreachCurrentValueVarName = CodeGenerationHelpers.AddLocalVariableToCurrentMethod(Context, node.Identifier.ValueText, Context.TypeResolver.Resolve(enumeratorCurrentMethod.GetMemberType())).VariableName;

            // Get the enumerator..
            Context.WriteNewLine();
            Context.WriteComment("variable to store the returned 'IEnumerator<T>'.");
            AddMethodCall(_ilVar, getEnumeratorMethod);
            var enumeratorVariableName = CodeGenerationHelpers.StoreTopOfStackInLocalVariable(Context, _ilVar, "enumerator", getEnumeratorMethod.ReturnType).VariableName;

            var endOfLoopLabelVar = Context.Naming.Label("endForEach");
            CreateCilInstruction(_ilVar, endOfLoopLabelVar, OpCodes.Nop);

            // loop while enumerable.MoveNext() == true
            var forEachLoopBegin = AddCilInstructionWithLocalVariable(_ilVar, OpCodes.Nop);

            var loadOpCode = getEnumeratorMethod.ReturnType.IsValueType || getEnumeratorMethod.ReturnType.TypeKind == TypeKind.TypeParameter ? OpCodes.Ldloca : OpCodes.Ldloc;
            //var loadOpCode = OpCodes.Ldloc;
            Context.EmitCilInstruction(_ilVar, loadOpCode, enumeratorVariableName);
            AddMethodCall(_ilVar, enumeratorMoveNextMethod);
            Context.EmitCilInstruction(_ilVar, OpCodes.Brfalse, endOfLoopLabelVar);
            
            Context.EmitCilInstruction(_ilVar, loadOpCode, enumeratorVariableName);
            AddMethodCall(_ilVar, enumeratorCurrentMethod);
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

        private IMethodSymbol GetEnumeratorMethodFor(ITypeSymbol enumerableType)
        {
            var interfacesToCheck = new[] { Context.RoslynTypeSystem.SystemCollectionsGenericIEnumerableOfT, Context.RoslynTypeSystem.SystemCollectionsIEnumerable };
            return GetMethodOnTypeOrImplementedInterfaces(enumerableType, interfacesToCheck, "GetEnumerator");
        }

        private IMethodSymbol GetMethodOnTypeOrImplementedInterfaces(ITypeSymbol inType, ITypeSymbol[] interfacesToCheck, string methodName)
        {
            var found = inType.GetMembers(methodName).SingleOrDefault();
            if (found != null)
                return (IMethodSymbol) found;

            int i = -1;
            while (found == null && ++i < interfacesToCheck.Length)
            {
                found = inType.Interfaces.SingleOrDefault(itf => SymbolEqualityComparer.Default.Equals(itf.OriginalDefinition, interfacesToCheck[i]))?.EnsureNotNull<ISymbol, ITypeSymbol>().GetMembers(methodName).SingleOrDefault();
            }

            return found.EnsureNotNull<ISymbol, IMethodSymbol>();
        }
        
        /*
         * MoveNext() method may be implemented in ...
         * 1. IEnumerator
         * 2. A type following the enumerator pattern
         */
        private IMethodSymbol MoveNextMethodFor(ITypeSymbol enumeratorType)
        {
            var interfacesToCheck = new[] { Context.RoslynTypeSystem.SystemCollectionsGenericIEnumeratorOfT, Context.RoslynTypeSystem.SystemCollectionsIEnumerator };
            return GetMethodOnTypeOrImplementedInterfaces(enumeratorType, interfacesToCheck, "MoveNext");
        }
        
        private IMethodSymbol CurrentMethodFor(ITypeSymbol enumeratorType)
        {
            var interfacesToCheck = new[] { Context.RoslynTypeSystem.SystemCollectionsGenericIEnumeratorOfT, Context.RoslynTypeSystem.SystemCollectionsIEnumerator };
            return GetMethodOnTypeOrImplementedInterfaces(enumeratorType, interfacesToCheck, "get_Current");
        }
        
        /*
         * either the type returned by GetEnumerator() implements `IEnumerator` interface *or*
         * it abides to the enumerator pattern, i.e, it has the following members:
         * 1. public bool MoveNext() method
         * 2. public T Current property ('T' can be any type)
         */
        private ITypeSymbol EnumeratorTypeFor(IMethodSymbol getEnumeratorMethod)
        {
            var moveNext = getEnumeratorMethod.ReturnType.GetMembers("MoveNext").SingleOrDefault();
            if (moveNext != null)
                return getEnumeratorMethod.ReturnType;
            
            if (SymbolEqualityComparer.Default.Equals(getEnumeratorMethod.ReturnType.OriginalDefinition, Context.RoslynTypeSystem.SystemCollectionsGenericIEnumeratorOfT))
                return getEnumeratorMethod.ReturnType;
            
            if (SymbolEqualityComparer.Default.Equals(getEnumeratorMethod.ReturnType.OriginalDefinition, Context.RoslynTypeSystem.SystemCollectionsIEnumerator))
                return getEnumeratorMethod.ReturnType;
            
            var enumeratorType = getEnumeratorMethod.ReturnType.Interfaces.SingleOrDefault(itf => itf.Name == "IEnumerator");
            return enumeratorType ?? getEnumeratorMethod.ReturnType;
        }
    }
}
