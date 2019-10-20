using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
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
            var eventType = ResolveType(node.Type);
            var eventDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName;
            var eventName = node.Identifier.ValueText;

            var eventDefVar = AddEventDefinition(eventName, eventType);
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

            var backingFieldVar = AddBackingField(node); // backing field will have same name as the event
            var isStatic = node.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
            var addAccessorVar = AddAddAccessor(node, backingFieldVar, isStatic);
            var removeAccessorVar = AddRemoveAccessor(node, backingFieldVar, isStatic);

            var eventDeclaringTypeVar = Context.DefinitionVariables.GetLastOf(MemberKind.Type).VariableName;
            AddCecilExpression($"{eventDeclaringTypeVar}.Methods.Add({addAccessorVar});");
            AddCecilExpression($"{eventDeclaringTypeVar}.Methods.Add({removeAccessorVar});");
            
            var eventName = node.Declaration.Variables[0].Identifier.Text;
            AddEventDefinition(eventDeclaringTypeVar, eventName, $"{backingFieldVar}.FieldType", addAccessorVar, removeAccessorVar);
        }

        private string AddRemoveAccessor(EventFieldDeclarationSyntax node, string backingFieldVar, bool isStatic)
        {
            if (node.Declaration.Variables.Count > 1)
                throw new NotSupportedException($"Only one event per declaration is supported.");

            var decl = node.Declaration.Variables[0];
            var addMethodVar = TempLocalVar($"{decl.Identifier.Text}_add");
            var accessorModifiers = node.Modifiers.MethodModifiersToCecil(ModifiersToCecil, "MethodAttributes.SpecialName");

            var addMethodExps = CecilDefinitionsFactory.Method(Context, addMethodVar, $"remove_{decl.Identifier.Text}", accessorModifiers, Context.TypeResolver.ResolvePredefinedType("Void"), Array.Empty<TypeParameterSyntax>());
            var paramsExps = AddParameterTo(addMethodVar, backingFieldVar);
            var localVarsExps = CreateLocalVarsForAddMethod(addMethodVar, backingFieldVar);
            var bodyExps = RemoveMethodBody(backingFieldVar, addMethodVar, node.Declaration.Type, isStatic);
            
            foreach (var exp in addMethodExps.Concat(paramsExps).Concat(bodyExps).Concat(localVarsExps))
            {
                WriteCecilExpression(Context, exp);
            }
            
            return addMethodVar;            
        }

        private string AddAddAccessor(EventFieldDeclarationSyntax node, string backingFieldVar, bool isStatic)
        {
            if (node.Declaration.Variables.Count > 1)
                throw new NotSupportedException($"Only one event per declaration is supported.");

            var decl = node.Declaration.Variables[0];
            var addMethodVar = TempLocalVar($"{decl.Identifier.Text}_add");
            var accessorModifiers = node.Modifiers.MethodModifiersToCecil(ModifiersToCecil, "MethodAttributes.SpecialName");

            var addMethodExps = CecilDefinitionsFactory.Method(Context, addMethodVar, $"add_{decl.Identifier.Text}", accessorModifiers, Context.TypeResolver.ResolvePredefinedType("Void"), Array.Empty<TypeParameterSyntax>());
            var paramsExps = AddParameterTo(addMethodVar, backingFieldVar);
            var localVarsExps = CreateLocalVarsForAddMethod(addMethodVar, backingFieldVar);
            var bodyExps = AddMethodBody(backingFieldVar, addMethodVar, node.Declaration.Type, isStatic);
            
            foreach (var exp in addMethodExps.Concat(paramsExps).Concat(bodyExps).Concat(localVarsExps))
            {
                WriteCecilExpression(Context, exp);
            }
            
            return addMethodVar;
        }

        private IEnumerable<string> CreateLocalVarsForAddMethod(string addMethodVar, string backingFieldVar)
        {
            for (int i = 0; i < 3; i++)
                yield return $"{addMethodVar}.Body.Variables.Add(new VariableDefinition({backingFieldVar}.FieldType));";
        }
        
        private IEnumerable<string> RemoveMethodBody(string backingFieldVar, string removeMethodVar, TypeSyntax eventType, bool isStatic)
        {
            var (ldfld, ldflda) = isStatic ? (OpCodes.Ldsfld, OpCodes.Ldsflda) : (OpCodes.Ldfld, OpCodes.Ldflda);
            
            var removeMethod = Utils.ImportFromMainModule("typeof(Delegate).GetMethod(\"Remove\")");
            var compareExchangeExps = CompareExchangeMethodResolvingExps(backingFieldVar, out var compExcVar);
            
            // static member access does not have a *this* so simply replace with *Nop*
            var lgarg_0 = isStatic ? OpCodes.Nop : OpCodes.Ldarg_0;
            var bodyExps = CecilDefinitionsFactory.MethodBody(removeMethodVar, new[]
            {
                lgarg_0,
                ldfld.WithOperand(backingFieldVar),
                OpCodes.Stloc_0,
                OpCodes.Ldloc_0.WithInstructionMarker("LoopStart"),
                OpCodes.Stloc_1,
                OpCodes.Ldloc_1,
                OpCodes.Ldarg.WithOperand(isStatic ? "0" : "1"),
                OpCodes.Call.WithOperand(removeMethod),
                OpCodes.Castclass.WithOperand(ResolveType(eventType)),
                OpCodes.Stloc_2,
                lgarg_0,
                ldflda.WithOperand(backingFieldVar),
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
        
        private IEnumerable<string> AddMethodBody(string backingFieldVar, string addMethodVar, TypeSyntax eventType, bool isStatic)
        {
            var (ldfld, ldflda) = isStatic ? (OpCodes.Ldsfld, OpCodes.Ldsflda) : (OpCodes.Ldfld, OpCodes.Ldflda);

            var combineMethod = Utils.ImportFromMainModule("typeof(Delegate).GetMethods().Single(m => m.Name == \"Combine\" && m.IsStatic && m.GetParameters().Length == 2)");
            var compareExchangeExps = CompareExchangeMethodResolvingExps(backingFieldVar, out var compExcVar);

            // static member access does not have a *this* so simply replace with *Nop*
            var lgarg_0 = isStatic ? OpCodes.Nop : OpCodes.Ldarg_0;
            var bodyExps = CecilDefinitionsFactory.MethodBody(addMethodVar, new[]
            {
                lgarg_0,
                ldfld.WithOperand(backingFieldVar),
                OpCodes.Stloc_0,
                OpCodes.Ldloc_0.WithInstructionMarker("LoopStart"),
                OpCodes.Stloc_1,
                OpCodes.Ldloc_1,
                OpCodes.Ldarg.WithOperand(isStatic ? "0" : "1"),
                OpCodes.Call.WithOperand(combineMethod),
                OpCodes.Castclass.WithOperand(ResolveType(eventType)),
                OpCodes.Stloc_2,
                lgarg_0,
                ldflda.WithOperand(backingFieldVar),
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

        private IEnumerable<string> CompareExchangeMethodResolvingExps(string backingFieldVar, out string compExcVar)
        {
            var openCompExcVar = TempLocalVar("openCompExch");
            var exp1 = $"var {openCompExcVar} = {Utils.ImportFromMainModule("typeof(System.Threading.Interlocked).GetMethods().Single(m => m.Name == \"CompareExchange\" && m.IsGenericMethodDefinition)")};";
            compExcVar = TempLocalVar("compExch");
            var exp2 = $"var {compExcVar} = new GenericInstanceMethod({openCompExcVar});";
            var exp3 = $"{compExcVar}.GenericArguments.Add({backingFieldVar}.FieldType);";
            
            return new[] {exp1, exp2, exp3 };
        }

        private IEnumerable<string> AddParameterTo(string methodVar, string backingFieldVar)
        {
            return new[]
            {
                $"{methodVar}.Parameters.Add(new ParameterDefinition(\"value\", ParameterAttributes.None, {backingFieldVar}.FieldType));",
            };
        }

        private string AddBackingField(EventFieldDeclarationSyntax node)
        {
            var privateModifier = SyntaxFactory.Token(SyntaxKind.PrivateKeyword); // always private no matter the accessibility of the event
            var accessibilityModifier = node.Modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword));
            var backingFieldModifiers = accessibilityModifier == default 
                ? node.Modifiers.Add(privateModifier) 
                : node.Modifiers.Replace(accessibilityModifier, privateModifier);

            var fields = FieldDeclarationVisitor.HandleFieldDeclaration(Context, node.Declaration, backingFieldModifiers, node.ResolveDeclaringType());
            return fields.First();
        }

        private void AddEventDefinition(string eventDeclaringTypeVar, string eventName, string eventType, string addAccessor, string removeAccessor)
        {
            var evtDefVar = TempLocalVar("evt");
            WriteCecilExpression(Context,$"var {evtDefVar} = new EventDefinition(\"{eventName}\", EventAttributes.None, {eventType});");
            WriteCecilExpression(Context,$"{evtDefVar}.AddMethod = {addAccessor};");
            WriteCecilExpression(Context,$"{evtDefVar}.RemoveMethod = {removeAccessor};");
            WriteCecilExpression(Context,$"{eventDeclaringTypeVar}.Events.Add({evtDefVar});");
        }
        
        private string AddEventDefinition(string eventName, string eventType)
        {
            var eventDefVar = TempLocalVar($"{eventName}DefVar");
            var eventDefExp = $"var {eventDefVar} = new EventDefinition(\"{eventName}\", EventAttributes.None, {eventType});";
            AddCecilExpression(eventDefExp);

            return eventDefVar;
        }
    }
}