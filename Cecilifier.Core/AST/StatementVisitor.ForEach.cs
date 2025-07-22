using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
    internal partial class StatementVisitor
    {
        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            ExpressionVisitor.Visit(Context, _ilVar, node.Expression);

            var enumerableType = Context.GetTypeInfo(node.Expression).Type.EnsureNotNull();
            if (enumerableType.TypeKind == TypeKind.Array)
                ProcessForEachOverArray();
            else
                ProcessForEachOverEnumerable();

            void ProcessForEachOverArray()
            {
                // save array in local variable...
                var arrayVariable = CodeGenerationHelpers.StoreTopOfStackInLocalVariable(Context, _ilVar, "array", enumerableType);
                
                var loopVariable = Context.AddLocalVariableToCurrentMethod(node.Identifier.ValueText, Context.TypeResolver.Resolve(enumerableType.ElementTypeSymbolOf())).VariableName;
                var loopIndexVar = Context.AddLocalVariableToCurrentMethod("index", Context.TypeResolver.Resolve(Context.RoslynTypeSystem.SystemInt32)).VariableName;

                var conditionCheckLabelVar = CreateCilInstruction(_ilVar, OpCodes.Nop);
                Context.EmitCilInstruction(_ilVar, OpCodes.Br, conditionCheckLabelVar);
                var firstLoopBodyInstructionVar = CreateCilInstruction(_ilVar, OpCodes.Ldloc, arrayVariable.VariableName);
                WriteCecilExpression(Context, $"{_ilVar}.Append({firstLoopBodyInstructionVar});");
                Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, loopIndexVar);
                Context.EmitCilInstruction(_ilVar, enumerableType.ElementTypeSymbolOf().LdelemOpCode());
                Context.EmitCilInstruction(_ilVar, OpCodes.Stloc, loopVariable);

                // Loop body.
                node.Statement.Accept(this);
                
                Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, loopIndexVar);
                Context.EmitCilInstruction(_ilVar, OpCodes.Ldc_I4_1);
                Context.EmitCilInstruction(_ilVar, OpCodes.Add);
                Context.EmitCilInstruction(_ilVar, OpCodes.Stloc, loopIndexVar);
                
                // condition check...
                WriteCecilExpression(Context, $"{_ilVar}.Append({conditionCheckLabelVar});");
                Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, loopIndexVar);
                Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, arrayVariable.VariableName);
                Context.EmitCilInstruction(_ilVar, OpCodes.Ldlen);
                Context.EmitCilInstruction(_ilVar, OpCodes.Conv_I4);
                Context.EmitCilInstruction(_ilVar, OpCodes.Blt, firstLoopBodyInstructionVar);
            }
            
            void ProcessForEachOverEnumerable()
            {
                var info = Context.SemanticModel.GetForEachStatementInfo(node);
                var enumeratorType = EnumeratorTypeFor(info.GetEnumeratorMethod);

                var isDisposable = enumeratorType.Interfaces.FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate, Context.RoslynTypeSystem.SystemIDisposable)) != null;

                var context = new ForEachHandlerContext(info.GetEnumeratorMethod, info.MoveNextMethod, info.CurrentProperty!);

                // Get the enumerator..
                // we need to do this here (as opposed to in ProcessForEach() method) because we have a enumerable instance in the stack 
                // and if this enumerable implements IDisposable we'll emit a try/finally but it is not valid to enter try/finally blocks
                // with a non empty stack.
                Context.WriteNewLine();
                Context.WriteComment("variable to store the returned 'IEnumerator<T>'.");
                Context.AddCallToMethod(context.GetEnumeratorMethod, _ilVar, MethodDispatchInformation.MostLikelyVirtual);
                context.EnumeratorVariableName = CodeGenerationHelpers.StoreTopOfStackInLocalVariable(Context, _ilVar, "enumerator", context.GetEnumeratorMethod.ReturnType).VariableName;

                if (isDisposable)
                {
                    ProcessWithInTryCatchFinallyBlock(
                        _ilVar,
                        context => ProcessForEach(context, node),
                        [],
                        ProcessForEachFinally,
                        context);
                }
                else
                {
                    ProcessForEach(context, node);
                }
            }
        }

        private void ProcessForEachFinally(ForEachHandlerContext forEachHandlerContext)
        {
            if (forEachHandlerContext.GetEnumeratorMethod.ReturnType.IsValueType || forEachHandlerContext.GetEnumeratorMethod.ReturnType.TypeKind == TypeKind.TypeParameter)
            {
                Context.EmitCilInstruction(_ilVar, OpCodes.Ldloca, forEachHandlerContext.EnumeratorVariableName);
                Context.EmitCilInstruction(_ilVar, OpCodes.Constrained, Context.TypeResolver.Resolve(forEachHandlerContext.GetEnumeratorMethod.ReturnType));
                Context.EmitCilInstruction(_ilVar, OpCodes.Callvirt, Context.RoslynTypeSystem.SystemIDisposable.GetMembers("Dispose").OfType<IMethodSymbol>().Single().MethodResolverExpression(Context));
            }
            else
            {
                var skipDisposeMethodCallNopVar = CreateCilInstruction(_ilVar, OpCodes.Nop);
                Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, forEachHandlerContext.EnumeratorVariableName);
                Context.EmitCilInstruction(_ilVar, OpCodes.Brfalse_S, skipDisposeMethodCallNopVar);
                Context.EmitCilInstruction(_ilVar, OpCodes.Ldloc, forEachHandlerContext.EnumeratorVariableName);
                Context.EmitCilInstruction(_ilVar, OpCodes.Callvirt, Context.RoslynTypeSystem.SystemIDisposable.GetMembers("Dispose").OfType<IMethodSymbol>().Single().MethodResolverExpression(Context));
                AddCecilExpression($"{_ilVar}.Append({skipDisposeMethodCallNopVar});");
            }
        }

        private void ProcessForEach(ForEachHandlerContext forEachHandlerContext, ForEachStatementSyntax node)
        {
            // Adds a variable to store current value in the foreach loop.
            Context.WriteNewLine();
            Context.WriteComment("variable to store current value in the foreach loop.");
            var foreachCurrentValueVarName = Context.AddLocalVariableToCurrentMethod(node.Identifier.ValueText, Context.TypeResolver.Resolve(forEachHandlerContext.EnumeratorCurrentProperty.GetMemberType())).VariableName;
            
            var endOfLoopLabelVar = Context.Naming.Label("endForEach");
            CreateCilInstruction(_ilVar, endOfLoopLabelVar, OpCodes.Nop);

            // loop while enumerable.MoveNext() == true
            var forEachLoopBegin = AddCilInstructionWithLocalVariable(_ilVar, OpCodes.Nop);

            var loadOpCode = forEachHandlerContext.GetEnumeratorMethod.ReturnType.IsValueType || forEachHandlerContext.GetEnumeratorMethod.ReturnType.TypeKind == TypeKind.TypeParameter ? OpCodes.Ldloca : OpCodes.Ldloc;
            Context.EmitCilInstruction(_ilVar, loadOpCode, forEachHandlerContext.EnumeratorVariableName);
            Context.AddCallToMethod(forEachHandlerContext.EnumeratorMoveNextMethod, _ilVar, MethodDispatchInformation.MostLikelyVirtual);
            Context.EmitCilInstruction(_ilVar, OpCodes.Brfalse, endOfLoopLabelVar);
            
            Context.EmitCilInstruction(_ilVar, loadOpCode, forEachHandlerContext.EnumeratorVariableName);
            Context.AddCallToMethod(forEachHandlerContext.EnumeratorCurrentProperty.GetMethod, _ilVar, MethodDispatchInformation.MostLikelyVirtual);

            if (!node.Type.IsKind(SyntaxKind.RefType) && forEachHandlerContext.EnumeratorCurrentProperty.IsByRef())
            {
                Context.EmitCilInstruction(_ilVar, forEachHandlerContext.EnumeratorCurrentProperty.Type.LdindOpCodeFor());
            }
            
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

        private record ForEachHandlerContext(IMethodSymbol GetEnumeratorMethod, IMethodSymbol EnumeratorMoveNextMethod, IPropertySymbol EnumeratorCurrentProperty)
        {
            public string EnumeratorVariableName { get; set; }
        }
    }
}
