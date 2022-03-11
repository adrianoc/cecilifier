using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

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
            
            eventDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Type).VariableName;

            var eventType = Context.TypeResolver.Resolve(eventSymbol.Type);
            var eventAccessorsDefVarMapping = new Dictionary<string, string>();
            foreach (var acc in node.AccessorList.Accessors)
            {
                using var _ = LineInformationTracker.Track(Context, acc);
                Context.WriteNewLine();
                Context.WriteComment($"{node.Identifier.ValueText} {acc.Keyword} method.");
                    
                var methodVar = Context.Naming.SyntheticVariable(acc.Keyword.ValueText, ElementKind.Method);
                var methodILVar = Context.Naming.ILProcessor(acc.Keyword.ValueText);
                var body = CecilDefinitionsFactory.MethodBody(methodVar, methodILVar, Array.Empty<InstructionRepresentation>());
                AddAccessor(node, eventSymbol, methodVar, acc.Keyword.ValueText, eventType, body);
                
                StatementVisitor.Visit(Context, methodILVar, acc.Body);
                Context.EmitCilInstruction(methodILVar, OpCodes.Ret);

                eventAccessorsDefVarMapping[acc.Keyword.ValueText] = methodVar;
            }

            Context.WriteNewLine();
            Context.WriteComment($"Event: {node.Identifier.Text}");
            var evtDefVar = AddEventDefinition(node, eventDeclaringTypeVar, node.Identifier.Text, eventType, eventAccessorsDefVarMapping["add"], eventAccessorsDefVarMapping["remove"]);
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
            
            eventDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(VariableMemberKind.Type).VariableName;

            var declaringType = node.ResolveDeclaringType<TypeDeclarationSyntax>();
            var backingFieldVar = declaringType.IsKind(SyntaxKind.InterfaceDeclaration) 
                ? string.Empty
                : AddBackingField(node); // backing field will have same name as the event
            
            var eventType = ResolveType(node.Declaration.Type);
            var addAccessorVar = AddAccessor(node, eventSymbol, "add", backingFieldVar, eventType, AddMethodBody);
            var removeAccessorVar = AddAccessor(node, eventSymbol, "remove", backingFieldVar, eventType, RemoveMethodBody);

            var evtDefVar = AddEventDefinition(node, eventDeclaringTypeVar, eventSymbol.Name, eventType, addAccessorVar, removeAccessorVar);
            HandleAttributesInMemberDeclaration(node.AttributeLists, evtDefVar);
        }

        private string AddAccessor(EventFieldDeclarationSyntax node, IEventSymbol eventSymbol, string accessorName, string backingFieldVar, string eventType, Func<EventFieldDeclarationSyntax, IEventSymbol, string, string, IEnumerable<string>> methodBodyFactory)
        {
            var methodVar = Context.Naming.SyntheticVariable(accessorName, ElementKind.Method);
            var isInterfaceDef = eventSymbol.ContainingType.TypeKind == TypeKind.Interface;
            IEnumerable<string> methodBodyExpressions = Array.Empty<string>();
            if (!isInterfaceDef)
            {
                var localVarsExps = CreateLocalVarsForAddMethod(methodVar, backingFieldVar);
                var bodyExps = methodBodyFactory(node, eventSymbol, backingFieldVar, methodVar);
                methodBodyExpressions  = methodBodyExpressions.Concat(bodyExps).Concat(localVarsExps);
            }
            
            return AddAccessor(node, eventSymbol, methodVar, accessorName, eventType, methodBodyExpressions);
        }
        
        private string AddAccessor(MemberDeclarationSyntax node, IEventSymbol eventSymbol, string methodVar, string accessorName, string eventType, IEnumerable<string> methodBodyExpressions)
        {
            var accessorModifiers = AccessModifiersForEventAccessors(node, eventSymbol.ContainingType.TypeKind == TypeKind.Interface);

            var methodName = $"{accessorName}_{eventSymbol.Name}";
            var methodExps = CecilDefinitionsFactory.Method(Context, methodVar, methodName, accessorModifiers, Context.RoslynTypeSystem.SystemVoid, false,Array.Empty<TypeParameterSyntax>());
            var paramsExps = AddParameterTo(methodVar, eventType);

            AddCecilExpressions(methodExps.Concat(paramsExps).Concat(methodBodyExpressions));
            AddCecilExpression($"{eventDeclaringTypeVar}.Methods.Add({methodVar});");
            Context.DefinitionVariables.RegisterMethod(eventSymbol.ContainingType.Name, methodName, new[] { eventSymbol.Type.Name }, methodVar);
            return methodVar;            
        }
        
        private static string AccessModifiersForEventAccessors(MemberDeclarationSyntax node, bool isInterfaceDef)
        {
            var accessorModifiers = isInterfaceDef 
                ? Constants.CommonCecilConstants.InterfaceMethodAttributes 
                : node.Modifiers.MethodModifiersToCecil((targetEnum, modifiers, defaultAccessibility) => ModifiersToCecil(modifiers, targetEnum, defaultAccessibility), "MethodAttributes.SpecialName");

            return accessorModifiers;
        }

        private IEnumerable<string> CreateLocalVarsForAddMethod(string methodVar, string backingFieldVar)
        {
            for (int i = 0; i < 3; i++)
                yield return $"{methodVar}.Body.Variables.Add(new VariableDefinition({backingFieldVar}.FieldType));";
        }
        
        private IEnumerable<string> RemoveMethodBody(EventFieldDeclarationSyntax context, IEventSymbol eventSymbol, string backingFieldVar, string removeMethodVar)
        {
            var isStatic = eventSymbol.IsStatic;
            var (ldfld, ldflda) = isStatic ? (OpCodes.Ldsfld, OpCodes.Ldsflda) : (OpCodes.Ldfld, OpCodes.Ldflda);
            
            var removeMethod = Utils.ImportFromMainModule("typeof(Delegate).GetMethod(\"Remove\")");
            var compareExchangeExps = CompareExchangeMethodResolvingExps(context, backingFieldVar, out var compExcVar);

            var fieldVar = Utils.MakeGenericTypeIfAppropriate(Context, eventSymbol, backingFieldVar, eventDeclaringTypeVar);

            // static member access does not have a *this* so simply replace with *Nop*
            var lgarg_0 = isStatic ? OpCodes.Nop : OpCodes.Ldarg_0;
            var bodyExps = CecilDefinitionsFactory.MethodBody(removeMethodVar, new[]
            {
                lgarg_0,
                ldfld.WithOperand(fieldVar),
                OpCodes.Stloc_0,
                OpCodes.Ldloc_0.WithInstructionMarker("LoopStart"),
                OpCodes.Stloc_1,
                OpCodes.Ldloc_1,
                OpCodes.Ldarg.WithOperand(isStatic ? "0" : "1"),
                OpCodes.Call.WithOperand(removeMethod),
                OpCodes.Castclass.WithOperand(Context.TypeResolver.Resolve(eventSymbol.Type)),
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
            });

            return compareExchangeExps.Concat(bodyExps);
        }
        
        private IEnumerable<string> AddMethodBody(EventFieldDeclarationSyntax context, IEventSymbol eventSymbol, string backingFieldVar, string addMethodVar)
        {
            var isStatic = eventSymbol.IsStatic;
            var (ldfld, ldflda) = isStatic ? (OpCodes.Ldsfld, OpCodes.Ldsflda) : (OpCodes.Ldfld, OpCodes.Ldflda);

            var combineMethod = Utils.ImportFromMainModule("typeof(Delegate).GetMethods().Single(m => m.Name == \"Combine\" && m.IsStatic && m.GetParameters().Length == 2)");
            var compareExchangeExps = CompareExchangeMethodResolvingExps(context, backingFieldVar, out var compExcVar);

            var fieldVar = Utils.MakeGenericTypeIfAppropriate(Context, eventSymbol, backingFieldVar, eventDeclaringTypeVar);

            // static member access does not have a *this* so simply replace with *Nop*
            var lgarg_0 = isStatic ? OpCodes.Nop : OpCodes.Ldarg_0;
            var bodyExps = CecilDefinitionsFactory.MethodBody(addMethodVar, new[]
            {
                lgarg_0,
                ldfld.WithOperand(fieldVar),
                OpCodes.Stloc_0,
                OpCodes.Ldloc_0.WithInstructionMarker("LoopStart"),
                OpCodes.Stloc_1,
                OpCodes.Ldloc_1,
                OpCodes.Ldarg.WithOperand(isStatic ? "0" : "1"),
                OpCodes.Call.WithOperand(combineMethod),
                OpCodes.Castclass.WithOperand(Context.TypeResolver.Resolve(eventSymbol.Type)),
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
            });
            
            return compareExchangeExps.Concat(bodyExps);
        }

        private IEnumerable<string> CompareExchangeMethodResolvingExps(CSharpSyntaxNode context, string backingFieldVar, out string compExcVar)
        {
            var openCompExcVar = Context.Naming.MemberReference("openCompExc");
            var exp1 = $"var {openCompExcVar} = {Utils.ImportFromMainModule("typeof(System.Threading.Interlocked).GetMethods().Single(m => m.Name == \"CompareExchange\" && m.IsGenericMethodDefinition)")};";

            compExcVar = Context.Naming.MemberReference("compExc");
            var exp2 = $"var {compExcVar} = new GenericInstanceMethod({openCompExcVar});";
            var exp3 = $"{compExcVar}.GenericArguments.Add({backingFieldVar}.FieldType);";
            
            return new[] {exp1, exp2, exp3 };
        }

        private IEnumerable<string> AddParameterTo(string methodVar, string fieldType)
        {
            return new[]
            {
                $"{methodVar}.Parameters.Add(new ParameterDefinition(\"value\", ParameterAttributes.None, {fieldType}));",
            };
        }

        private string AddBackingField(EventFieldDeclarationSyntax node)
        {
            var privateModifier = SyntaxFactory.Token(SyntaxKind.PrivateKeyword); // always private no matter the accessibility of the event
            var noAccessibilityModifier = node.Modifiers.Where(m => !m.IsKind(SyntaxKind.PublicKeyword) 
                                                                    && !m.IsKind(SyntaxKind.PrivateKeyword)
                                                                    && !m.IsKind(SyntaxKind.InternalKeyword)
                                                                    && !m.IsKind(SyntaxKind.ProtectedKeyword));
            
            var fields = FieldDeclarationVisitor.HandleFieldDeclaration(Context, node, node.Declaration, noAccessibilityModifier.Append(privateModifier).ToList(), node.ResolveDeclaringType<TypeDeclarationSyntax>());
            return fields.First();
        }

        private string AddEventDefinition(MemberDeclarationSyntax eventFieldDeclaration, string eventDeclaringTypeVar, string eventName, string eventType, string addAccessor, string removeAccessor)
        {
            var evtDefVar = Context.Naming.EventDeclaration(eventFieldDeclaration);
            WriteCecilExpression(Context,$"var {evtDefVar} = new EventDefinition(\"{eventName}\", EventAttributes.None, {eventType});");
            WriteCecilExpression(Context,$"{evtDefVar}.AddMethod = {addAccessor};");
            WriteCecilExpression(Context,$"{evtDefVar}.RemoveMethod = {removeAccessor};");
            WriteCecilExpression(Context,$"{eventDeclaringTypeVar}.Events.Add({evtDefVar});");

            return evtDefVar;
        }
        
        private string AddEventDefinition(EventDeclarationSyntax eventDeclarationSyntax, string eventName, string eventType)
        {
            var eventDefVar = Context.Naming.EventDeclaration(eventDeclarationSyntax);
            var eventDefExp = $"var {eventDefVar} = new EventDefinition(\"{eventName}\", EventAttributes.None, {eventType});";
            AddCecilExpression(eventDefExp);
            WriteCecilExpression(Context,$"{eventDeclaringTypeVar}.Events.Add({eventDefVar});");

            return eventDefVar;
        }

        private string eventDeclaringTypeVar;
    }
}
