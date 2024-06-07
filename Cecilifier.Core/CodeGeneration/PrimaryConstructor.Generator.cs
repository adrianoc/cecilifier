using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.AST;
using Cecilifier.Core.CodeGeneration.Extensions;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.CodeGeneration;

/// <summary>
/// Only primary constructors on *record*s are supported.
/// </summary>
public class PrimaryConstructorGenerator
{
    internal static void AddPropertiesFrom(IVisitorContext context, string typeDefinitionVariable, TypeDeclarationSyntax type)
    {
        if (type.ParameterList is null)
            return;

        foreach (var parameter in type.GetUniqueParameters(context))
        {
            AddPropertyFor(context, parameter, typeDefinitionVariable);
            context.WriteNewLine();
        }
    }

    private static void AddPropertyFor(IVisitorContext context, ParameterSyntax parameter, string typeDefinitionVariable)
    {
        using var _ = LineInformationTracker.Track(context, parameter);
        
        context.WriteComment($"Property: {parameter.Identifier.Text} (primary constructor)");
        var propDefVar = context.Naming.SyntheticVariable(parameter.Identifier.Text, ElementKind.Property);
        var paramSymbol = context.SemanticModel.GetDeclaredSymbol(parameter).EnsureNotNull<ISymbol, IParameterSymbol>();
        var exps = CecilDefinitionsFactory.PropertyDefinition(propDefVar, parameter.Identifier.Text, context.TypeResolver.Resolve(paramSymbol.Type));
        
        context.WriteCecilExpressions(exps);
        context.WriteCecilExpression($"{typeDefinitionVariable}.Properties.Add({propDefVar});");
        context.WriteNewLine();
        context.WriteNewLine();

        var declaringTypeVariable = context.DefinitionVariables.GetLastOf(VariableMemberKind.Type);
        if (!declaringTypeVariable.IsValid)
            throw new InvalidOperationException();

        var publicPropertyMethodAttributes = "MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName";
        var propertyType = context.SemanticModel.GetDeclaredSymbol(parameter).GetMemberType();
        var propertyData = new PropertyGenerationData(
                                    declaringTypeVariable.MemberName,
                                    declaringTypeVariable.VariableName,
                                    false, // TODO
                                    propDefVar,
                                    parameter.Identifier.Text,
                                    new Dictionary<string, string>
                                    {
                                        ["get"] = publicPropertyMethodAttributes,
                                        ["set"] = publicPropertyMethodAttributes
                                    },
                                    false,
                                    context.TypeResolver.Resolve(propertyType),
                                    propertyType.ToDisplayString(),
                                    Array.Empty<ParameterSpec>(),
                                    "FieldAttributes.Private", //TODO: Constant
                                    OpCodes.Stfld,
                                    OpCodes.Ldfld);
        
        PropertyGenerator propertyGenerator = new (context);
        
        AddGetter();
        context.WriteNewLine();
        AddInit();

        void AddGetter()
        {
            context.WriteComment($"{propertyData.Name} getter");
            var getMethodVar = context.Naming.SyntheticVariable($"get{propertyData.Name}", ElementKind.Method);
            // properties for primary ctor parameters cannot override base properties, so hasCovariantReturn = false and overridenMethod = null (none)
            using (propertyGenerator.AddGetterMethodDeclaration(in propertyData, getMethodVar, false, $"get_{propertyData.Name}", null))
            {
                var ilVar = context.Naming.ILProcessor($"get{propertyData.Name}");
                context.WriteCecilExpressions([$"var {ilVar} = {getMethodVar}.Body.GetILProcessor();"]);
                
                propertyGenerator.AddAutoGetterMethodImplementation(in propertyData, ilVar, getMethodVar);
            }
        }
        
        void AddInit()
        {
            context.WriteComment($"{propertyData.Name} init");
            var setMethodVar = context.Naming.SyntheticVariable($"set{propertyData.Name}", ElementKind.Method);
            using (propertyGenerator.AddSetterMethodDeclaration(in propertyData, setMethodVar, true, $"set_{propertyData.Name}", null))
            {
                var ilVar = context.Naming.ILProcessor($"set{propertyData.Name}");
                context.WriteCecilExpressions([$"var {ilVar} = {setMethodVar}.Body.GetILProcessor();"]);
                
                propertyGenerator.AddAutoSetterMethodImplementation(in propertyData, ilVar, setMethodVar);
                context.EmitCilInstruction(ilVar, OpCodes.Ret);
            }
        }
    }

    internal static void AddPrimaryConstructor(IVisitorContext context, string recordTypeDefinitionVariable, TypeDeclarationSyntax typeDeclaration)
    {
        context.WriteComment($"Constructor: {typeDeclaration.Identifier.ValueText()}{typeDeclaration.ParameterList}");
        var ctorVar = context.Naming.Constructor(typeDeclaration, false);
        var ctorExp = CecilDefinitionsFactory.Constructor(
                                        context, 
                                        ctorVar, 
                                        typeDeclaration.Identifier.ValueText, 
                                        false, 
                                        "MethodAttributes.Public", 
                                        typeDeclaration.ParameterList?.Parameters.Select(p => p.Type?.ToString()).ToArray() ?? []);

        context.WriteCecilExpressions(
        [
            ctorExp,
            $"{recordTypeDefinitionVariable}.Methods.Add({ctorVar});"
        ]);

        if (typeDeclaration.ParameterList?.Parameters == null)
            return;
        
        var ctorIlVar = context.Naming.ILProcessor($"ctor_{typeDeclaration.Identifier.ValueText}");
        var ctorExps = CecilDefinitionsFactory.MethodBody(context.Naming, $"ctor_{typeDeclaration.Identifier.ValueText}", ctorVar, ctorIlVar, [], []);
        context.WriteCecilExpressions(ctorExps);

        var uniqueParameters = typeDeclaration.GetUniqueParameters(context).ToHashSet();
        foreach (var parameter in typeDeclaration.ParameterList.Parameters)
        {
            context.WriteComment($"Parameter: {parameter.Identifier}");
            var paramVar = context.Naming.Parameter(parameter);
            var parameterType = context.TypeResolver.Resolve(ModelExtensions.GetTypeInfo(context.SemanticModel, parameter.Type!).Type);
            var paramExps = CecilDefinitionsFactory.Parameter(parameter.Identifier.ValueText, RefKind.None, false, ctorVar, paramVar, parameterType, Constants.ParameterAttributes.None, ("", false));
            context.WriteCecilExpressions(paramExps);

            if (!uniqueParameters.Contains(parameter))
                continue;
            
            context.EmitCilInstruction(ctorIlVar, OpCodes.Ldarg_0);
            context.EmitCilInstruction(ctorIlVar, OpCodes.Ldarg, paramVar);

            var backingFieldVar = context.DefinitionVariables.GetVariable(Utils.BackingFieldNameForAutoProperty(parameter.Identifier.ValueText), VariableMemberKind.Field, typeDeclaration.Identifier.ValueText);
            if (!backingFieldVar.IsValid)
                throw new InvalidOperationException($"Backing field variable for property '{parameter.Identifier.ValueText}' could not be found.");

            context.EmitCilInstruction(ctorIlVar, OpCodes.Stfld, backingFieldVar.VariableName);
        }

        InvokeBaseConstructor(context, ctorIlVar, typeDeclaration);
        context.EmitCilInstruction(ctorIlVar, OpCodes.Ret);

        static void InvokeBaseConstructor(IVisitorContext context, string ctorIlVar, TypeDeclarationSyntax typeDeclaration)
        {
            var baseCtor = string.Empty;
            context.EmitCilInstruction(ctorIlVar, OpCodes.Ldarg_0);
        
            var primaryConstructorBase = typeDeclaration.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>().SingleOrDefault();
            if (primaryConstructorBase != null)
            {
                // resolve base constructor and load arguments to pass to base primary constructor
                baseCtor = context.SemanticModel.GetSymbolInfo(primaryConstructorBase).Symbol.EnsureNotNull<ISymbol, IMethodSymbol>().MethodResolverExpression(context);
                foreach (var argument in primaryConstructorBase.ArgumentList.Arguments)
                {
                    ExpressionVisitor.Visit(context, ctorIlVar, argument);
                }
            }
            else
            {
                baseCtor = context.RoslynTypeSystem.SystemObject.GetMembers().OfType<IMethodSymbol>().Single(m => m is { Name: ".ctor" }).MethodResolverExpression(context);
            }
        
            context.EmitCilInstruction(ctorIlVar, OpCodes.Call, baseCtor);
        }
    }
}
