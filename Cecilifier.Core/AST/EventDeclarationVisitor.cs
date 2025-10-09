using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;

namespace Cecilifier.Core.AST
{
    internal class EventDeclarationVisitor : SyntaxWalkerBase
    {
        public EventDeclarationVisitor(IVisitorContext context) : base(context)
        {
        }

        /*
         * Handles events with accessors, for instance
         *
         * class Foo
         * {
         *     public event EventHandler TheEvent
         *     {
         *         add {}
         *         remove {}
         *     }
         * }
         */
        public override void VisitEventDeclaration(EventDeclarationSyntax node)
        {
            using var __ = LineInformationTracker.Track(Context, node);
            var eventSymbol = Context.SemanticModel.GetDeclaredSymbol(node).EnsureNotNull<ISymbol, IEventSymbol>();

            eventDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Type);

            var eventType = Context.TypeResolver.ResolveAny(eventSymbol.Type);
            var eventAccessorsDefVarMapping = new Dictionary<string, string>();
            foreach (var acc in node.AccessorList.Accessors)
            {
                using var _ = LineInformationTracker.Track(Context, acc);
                Context.WriteNewLine();
                Context.WriteComment($"{node.Identifier.ValueText} {acc.Keyword} method.");

                var methodVar = Context.Naming.SyntheticVariable(acc.Keyword.ValueText, ElementKind.Method);
                var ilContext = Context.ApiDriver.NewIlContext(Context, acc.Keyword.ValueText, methodVar);
                var body = Context.ApiDefinitionsFactory.MethodBody(Context, acc.Keyword.ValueText, ilContext, [], []);
                var accessorMethodVar = AddAccessor(node, eventSymbol, methodVar, acc.Keyword.ValueText, eventType, body);
                using (Context.DefinitionVariables.WithVariable(accessorMethodVar))
                {
                    StatementVisitor.Visit(Context, ilContext.VariableName, acc.Body);
                    Context.ApiDriver.WriteCilInstruction(Context, ilContext.VariableName, OpCodes.Ret);
                }

                eventAccessorsDefVarMapping[acc.Keyword.ValueText] = methodVar;
            }

            Context.WriteNewLine();
            Context.WriteComment($"Event: {node.Identifier.Text}");
            var evtDefVar = AddEventDefinition(node, eventDeclaringTypeVar.VariableName, node.Identifier.Text, eventType, eventAccessorsDefVarMapping["add"], eventAccessorsDefVarMapping["remove"]);
            HandleAttributesInMemberDeclaration(node.AttributeLists, evtDefVar);
        }

        // Handles field like events (i.e, no add/remove accessors)
        public override void VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
        {
            /*
            .field private class [mscorlib]System.EventHandler TheEvent
            .event [mscorlib]System.EventHandler TheEvent
            {
		        .addon instance void Test::add_TheEvent(class [mscorlib]System.EventHandler)
		        .removeon instance void Test::remove_TheEvent(class [mscorlib]System.EventHandler)
            }
            */

            using var _ = LineInformationTracker.Track(Context, node);
            Context.WriteNewLine();
            Context.WriteComment($"Event: {node.Declaration.Variables.First().Identifier.Text}");

            var eventSymbol = (IEventSymbol) Context.SemanticModel.GetDeclaredSymbol(node.Declaration.Variables[0]);

            eventDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Type);

            var declaringType = node.ResolveDeclaringType<TypeDeclarationSyntax>();
            var backingFieldVar = declaringType.IsKind(SyntaxKind.InterfaceDeclaration)
                ? string.Empty
                : AddBackingField(node); // backing field will have same name as the event

            var eventType = ResolveType(node.Declaration.Type, ResolveTargetKind.None);
            var addAccessorVar = AddAccessor(node, eventSymbol, "add", backingFieldVar, eventType, AddMethodBody);
            var removeAccessorVar = AddAccessor(node, eventSymbol, "remove", backingFieldVar, eventType, RemoveMethodBody);

            var evtDefVar = AddEventDefinition(node, eventDeclaringTypeVar.VariableName, eventSymbol.Name, eventType, addAccessorVar, removeAccessorVar);
            HandleAttributesInMemberDeclaration(node.AttributeLists, evtDefVar);
        }

        private string AddAccessor(EventFieldDeclarationSyntax node, IEventSymbol eventSymbol, string accessorName, string backingFieldVar, string eventType, Func<EventFieldDeclarationSyntax, IEventSymbol, string, string, string, IEnumerable<string>> methodBodyFactory)
        {
            var methodVar = Context.Naming.SyntheticVariable(accessorName, ElementKind.Method);
            var isInterfaceDef = eventSymbol.ContainingType.TypeKind == TypeKind.Interface;
            IEnumerable<string> methodBodyExpressions = Array.Empty<string>();
            if (!isInterfaceDef)
            {
                var localVarsExps = CreateLocalVarsForEventRegistrationMethods(methodVar, backingFieldVar);
                methodBodyExpressions = methodBodyFactory(node, eventSymbol, accessorName, methodVar, backingFieldVar)
                                                .Concat(localVarsExps)
                                                .Append($"{methodVar}.Body.InitLocals = true;");
            }

            AddAccessor(node, eventSymbol, methodVar, accessorName, eventType, methodBodyExpressions);
            return methodVar;
        }

        private MethodDefinitionVariable AddAccessor(MemberDeclarationSyntax node, IEventSymbol eventSymbol, string methodVar, string accessorName, string eventType, IEnumerable<string> methodBodyExpressions)
        {
            var accessorModifiers = AccessModifiersForEventAccessors(node, eventSymbol.ContainingType);
            var methodName = $"{accessorName}_{eventSymbol.Name}";
            var methodExps = Context.ApiDefinitionsFactory.Method(
                                                                                Context, 
                                                                                new BodiedMemberDefinitionContext(methodName, methodVar, eventDeclaringTypeVar.VariableName, eventSymbol.IsStatic ? MemberOptions.Static : MemberOptions.None, IlContext.None), 
                                                                                eventDeclaringTypeVar.MemberName, 
                                                                                methodName, 
                                                                                methodName, 
                                                                                accessorModifiers, 
                                                                                [ new ParameterSpec("value", eventType, RefKind.None, Constants.ParameterAttributes.None) { RegistrationTypeName = eventSymbol.Type.ToDisplayString() }],
                                                                                [], 
                                                                                ctx => ctx.TypeResolver.ResolveAny(Context.RoslynTypeSystem.SystemVoid, ResolveTargetKind.ReturnType),  
                                                                                out var eventAccessorMethodVar);

            AddCecilExpressions(Context, methodExps.Concat(methodBodyExpressions));
            return eventAccessorMethodVar;
        }

        private static string AccessModifiersForEventAccessors(MemberDeclarationSyntax node, ITypeSymbol typeSymbol)
        {
            return node.Modifiers.ModifiersForSyntheticMethod("MethodAttributes.SpecialName", typeSymbol);
        }

        private IEnumerable<string> CreateLocalVarsForEventRegistrationMethods(string methodVar, string backingFieldVar)
        {
            for (int i = 0; i < 3; i++)
                yield return $"{methodVar}.Body.Variables.Add(new VariableDefinition({backingFieldVar}.FieldType));";
        }

        private IEnumerable<string> RemoveMethodBody(EventFieldDeclarationSyntax context, IEventSymbol eventSymbol, string accessorName, string removeMethodVar, string backingFieldVar)
        {
            var isStatic = eventSymbol.IsStatic;
            var (ldfld, ldflda) = isStatic ? (OpCodes.Ldsfld, OpCodes.Ldsflda) : (OpCodes.Ldfld, OpCodes.Ldflda);

            var removeMethod = Utils.ImportFromMainModule("typeof(Delegate).GetMethod(\"Remove\")");
            var compareExchangeExps = CompareExchangeMethodResolvingExps(backingFieldVar, out var compExcVar);

            var fieldVar = Utils.MakeGenericTypeIfAppropriate(Context, eventSymbol, backingFieldVar, eventDeclaringTypeVar.VariableName);

            // static member access does not have a *this* so simply replace with *Nop*
            var lgarg_0 = isStatic ? OpCodes.Nop : OpCodes.Ldarg_0;
            string[] localVariableTypes = [];
            InstructionRepresentation[] instructions = [
                lgarg_0,
                ldfld.WithOperand(fieldVar),
                OpCodes.Stloc_0,
                OpCodes.Ldloc_0.WithInstructionMarker("LoopStart"),
                OpCodes.Stloc_1,
                OpCodes.Ldloc_1,
                OpCodes.Ldarg.WithOperand(isStatic ? "0" : "1"),
                OpCodes.Call.WithOperand(removeMethod),
                OpCodes.Castclass.WithOperand(Context.TypeResolver.ResolveAny(eventSymbol.Type)),
                OpCodes.Stloc_2,
                lgarg_0,
                ldflda.WithOperand(fieldVar),
                OpCodes.Ldloc_2,
                OpCodes.Ldloc_1,
                OpCodes.Call.WithOperand(compExcVar),
                OpCodes.Stloc_0,
                OpCodes.Ldloc_0,
                OpCodes.Ldloc_1,
                OpCodes.Bne_Un_S.WithBranchOperand("LoopStart"),
                OpCodes.Ret
            ];
            var bodyExps = Context.ApiDefinitionsFactory.MethodBody(Context, accessorName, Context.ApiDriver.NewIlContext(Context, accessorName, removeMethodVar), localVariableTypes, instructions);

            return compareExchangeExps.Concat(bodyExps);
        }

        private IEnumerable<string> AddMethodBody(EventFieldDeclarationSyntax context, IEventSymbol eventSymbol, string accessorName, string addMethodVar, string backingFieldVar)
        {
            var isStatic = eventSymbol.IsStatic;
            var (ldfld, ldflda) = isStatic ? (OpCodes.Ldsfld, OpCodes.Ldsflda) : (OpCodes.Ldfld, OpCodes.Ldflda);

            var combineMethod = Utils.ImportFromMainModule("typeof(Delegate).GetMethods().Single(m => m.Name == \"Combine\" && m.IsStatic && m.GetParameters().Length == 2)");
            var compareExchangeExps = CompareExchangeMethodResolvingExps(backingFieldVar, out var compExcVar);

            var fieldVar = Utils.MakeGenericTypeIfAppropriate(Context, eventSymbol, backingFieldVar, eventDeclaringTypeVar.VariableName);

            // static member access does not have a *this* so simply replace with *Nop*
            var lgarg_0 = isStatic ? OpCodes.Nop : OpCodes.Ldarg_0;
            string[] localVariableTypes = [];
            InstructionRepresentation[] instructions = [
                lgarg_0,
                ldfld.WithOperand(fieldVar),
                OpCodes.Stloc_0,
                OpCodes.Ldloc_0.WithInstructionMarker("LoopStart"),
                OpCodes.Stloc_1,
                OpCodes.Ldloc_1,
                OpCodes.Ldarg.WithOperand(isStatic ? "0" : "1"),
                OpCodes.Call.WithOperand(combineMethod),
                OpCodes.Castclass.WithOperand(Context.TypeResolver.ResolveAny(eventSymbol.Type)),
                OpCodes.Stloc_2,
                lgarg_0,
                ldflda.WithOperand(fieldVar),
                OpCodes.Ldloc_2,
                OpCodes.Ldloc_1,
                OpCodes.Call.WithOperand(compExcVar),
                OpCodes.Stloc_0,
                OpCodes.Ldloc_0,
                OpCodes.Ldloc_1,
                OpCodes.Bne_Un_S.WithBranchOperand("LoopStart"),
                OpCodes.Nop,
                OpCodes.Ret
            ];
            var bodyExps = Context.ApiDefinitionsFactory.MethodBody(Context, accessorName, Context.ApiDriver.NewIlContext(Context, accessorName, addMethodVar), localVariableTypes, instructions);

            return compareExchangeExps.Concat(bodyExps);
        }

        private IEnumerable<string> CompareExchangeMethodResolvingExps(string backingFieldVar, out string compExcVar)
        {
            var openCompExcVar = Context.Naming.MemberReference("openCompExc");
            var exp1 = $"var {openCompExcVar} = {Utils.ImportFromMainModule("typeof(System.Threading.Interlocked).GetMethods().Single(m => m.Name == \"CompareExchange\" && m.IsGenericMethodDefinition)")};";

            return new[] { exp1 }.Concat(openCompExcVar.MakeGenericInstanceMethod(Context, "compExp", [$"{backingFieldVar}.FieldType"], out compExcVar));
        }

        private string AddBackingField(EventFieldDeclarationSyntax node)
        {
            var privateModifier = SyntaxFactory.Token(SyntaxKind.PrivateKeyword); // always private no matter the accessibility of the event
            var cleanedUpModifiers = node.Modifiers
                                                                    .Where(m =>
                                                                        !m.IsKind(SyntaxKind.PublicKeyword)
                                                                        && !m.IsKind(SyntaxKind.PrivateKeyword)
                                                                        && !m.IsKind(SyntaxKind.InternalKeyword)
                                                                        && !m.IsKind(SyntaxKind.ProtectedKeyword)
                                                                        && !m.IsKind(SyntaxKind.VirtualKeyword)) // There's no such thing as a virtual field
                                                                    .Append(privateModifier)
                                                                    .ToList();

            var fields = FieldDeclarationVisitor.HandleFieldDeclaration(Context, node, node.Declaration, cleanedUpModifiers, node.ResolveDeclaringType<TypeDeclarationSyntax>());
            return fields.First();
        }

        private string AddEventDefinition(MemberDeclarationSyntax eventFieldDeclaration, string eventDeclaringTypeVar, string eventName, string eventType, string addAccessor, string removeAccessor)
        {
            var evtDefVar = Context.Naming.EventDeclaration(eventFieldDeclaration);
            WriteCecilExpression(Context, $"var {evtDefVar} = new EventDefinition(\"{eventName}\", EventAttributes.None, {eventType});");
            WriteCecilExpression(Context, $"{evtDefVar}.AddMethod = {addAccessor};");
            WriteCecilExpression(Context, $"{evtDefVar}.RemoveMethod = {removeAccessor};");
            WriteCecilExpression(Context, $"{eventDeclaringTypeVar}.Events.Add({evtDefVar});");

            return evtDefVar;
        }

        private DefinitionVariable eventDeclaringTypeVar;
    }
}
