using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
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
            Context.WriteNewLine();
            Context.WriteComment($"Event: {node.Identifier.Text}");
            
            var eventType = ResolveType(node.Type);
            var eventDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName;
            var eventName = node.Identifier.ValueText;

            var eventDefVar = AddEventDefinition(node, eventName, eventType);
            AddCecilExpression($"{eventDeclaringTypeVar}.Properties.Add({eventDefVar});");
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

            Context.WriteNewLine();
            Context.WriteComment($"Event: {node.Declaration.Variables.First().Identifier.Text}");

            var eventSymbol = (IEventSymbol) Context.SemanticModel.GetDeclaredSymbol(node.Declaration.Variables[0]);
            
            eventDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName;

            var backingFieldVar = string.Empty;

            var declaringType = node.ResolveDeclaringType<TypeDeclarationSyntax>();
            if (!declaringType.IsKind(SyntaxKind.InterfaceDeclaration))
            {
                backingFieldVar = AddBackingField(node); // backing field will have same name as the event
            }
            
            var eventType = ResolveType(node.Declaration.Type);

            var addAccessorVar = AddAddAccessor(node, eventSymbol, backingFieldVar, eventType);
            var removeAccessorVar = AddRemoveAccessor(node, eventSymbol, backingFieldVar, eventType);

            AddCecilExpression($"{eventDeclaringTypeVar}.Methods.Add({addAccessorVar});");
            AddCecilExpression($"{eventDeclaringTypeVar}.Methods.Add({removeAccessorVar});");
            
            var eventName = node.Declaration.Variables[0].Identifier.Text;
            
            var evtDefVar = AddEventDefinition(node, eventDeclaringTypeVar, eventName, eventType, addAccessorVar, removeAccessorVar);
            HandleAttributesInMemberDeclaration(node.AttributeLists, evtDefVar);
        }

        private string AddRemoveAccessor(EventFieldDeclarationSyntax node, IEventSymbol eventSymbol, string backingFieldVar, string eventType)
        {
            var removeMethodVar = Context.Naming.SyntheticVariable("remove", ElementKind.Method);
            var isInterfaceDef = eventSymbol.ContainingType.TypeKind == TypeKind.Interface;
            var accessorModifiers = AccessModifiersForEventAccessors(node, isInterfaceDef);

            var removeMethodExps = CecilDefinitionsFactory.Method(Context, removeMethodVar, $"remove_{eventSymbol.Name}", accessorModifiers, GetSpecialType(SpecialType.System_Void), false,Array.Empty<TypeParameterSyntax>());
            var paramsExps = AddParameterTo(removeMethodVar, eventType);

            removeMethodExps = removeMethodExps.Concat(paramsExps);
            if (!isInterfaceDef)
            {
                var localVarsExps = CreateLocalVarsForAddMethod(removeMethodVar, backingFieldVar);
                var bodyExps = RemoveMethodBody(node, eventSymbol, backingFieldVar, removeMethodVar);
                removeMethodExps  = removeMethodExps.Concat(bodyExps).Concat(localVarsExps);
            }
            
            foreach (var exp in removeMethodExps)
            {
                WriteCecilExpression(Context, exp);
            }
            
            return removeMethodVar;            
        }

        private string AddAddAccessor(EventFieldDeclarationSyntax node, IEventSymbol eventSymbol, string backingFieldVar, string eventType)
        {
            var addMethodVar = Context.Naming.SyntheticVariable("add", ElementKind.Method);
            var isInterfaceDef = eventSymbol.ContainingType.TypeKind == TypeKind.Interface;
            
            var accessorModifiers = AccessModifiersForEventAccessors(node, isInterfaceDef);
            var addMethodExps = CecilDefinitionsFactory.Method(Context, addMethodVar, $"add_{eventSymbol.Name}", accessorModifiers, GetSpecialType(SpecialType.System_Void), false, Array.Empty<TypeParameterSyntax>());

            var paramsExps = AddParameterTo(addMethodVar, eventType);
            addMethodExps = addMethodExps.Concat(paramsExps);
            
            if (!isInterfaceDef)
            {
                var localVarsExps = CreateLocalVarsForAddMethod(addMethodVar, backingFieldVar);
                var bodyExps = AddMethodBody(node, eventSymbol, backingFieldVar, addMethodVar);

                addMethodExps = addMethodExps.Concat(bodyExps).Concat(localVarsExps);
            }

            foreach (var exp in addMethodExps)
            {
                WriteCecilExpression(Context, exp);
            }

            return addMethodVar;
        }

        private static string AccessModifiersForEventAccessors(EventFieldDeclarationSyntax node, bool isInterfaceDef)
        {
            string accessorModifiers;
            if (isInterfaceDef)
            {
                accessorModifiers = "MethodAttributes.SpecialName | MethodAttributes.Public | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.Abstract | MethodAttributes.HideBySig";
            }
            else
            {
                accessorModifiers = node.Modifiers.MethodModifiersToCecil(ModifiersToCecil, "MethodAttributes.SpecialName");
            }

            return accessorModifiers;
        }

        private IEnumerable<string> CreateLocalVarsForAddMethod(string addMethodVar, string backingFieldVar)
        {
            for (int i = 0; i < 3; i++)
                yield return $"{addMethodVar}.Body.Variables.Add(new VariableDefinition({backingFieldVar}.FieldType));";
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
            var openCompExcVar = Context.Naming.MemberReference("openCompExc",null);
            var exp1 = $"var {openCompExcVar} = {Utils.ImportFromMainModule("typeof(System.Threading.Interlocked).GetMethods().Single(m => m.Name == \"CompareExchange\" && m.IsGenericMethodDefinition)")};";

            compExcVar = Context.Naming.MemberReference("compExc", null);
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
            var accessibilityModifier = node.Modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword));
            var backingFieldModifiers = accessibilityModifier == default 
                ? node.Modifiers.Add(privateModifier) 
                : node.Modifiers.Replace(accessibilityModifier, privateModifier);

            var fields = FieldDeclarationVisitor.HandleFieldDeclaration(Context, node, node.Declaration, backingFieldModifiers, node.ResolveDeclaringType<TypeDeclarationSyntax>());
            return fields.First();
        }

        private string AddEventDefinition(EventFieldDeclarationSyntax eventFieldDeclaration, string eventDeclaringTypeVar, string eventName, string eventType, string addAccessor, string removeAccessor)
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

            return eventDefVar;
        }

        private string eventDeclaringTypeVar;
    }
}
