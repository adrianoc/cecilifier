using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Cecilifier.Core.ApiDriver;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cecilifier.Core.AST;
using Cecilifier.Core.CodeGeneration.Extensions;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Mappings;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;

namespace Cecilifier.Core.CodeGeneration;

/// <summary>
/// Only primary constructors on *record*s are supported.
/// </summary>
public class PrimaryConstructorGenerator
{
    internal static void AddPropertiesFrom(IVisitorContext context, string typeDefinitionVariable, TypeDeclarationSyntax type, INamedTypeSymbol declaringType)
    {
        if (type.ParameterList is null)
            return;

        foreach (var parameter in type.GetUniqueParameters(context))
        {
            AddPropertyFor(context, parameter, typeDefinitionVariable, declaringType);
            context.WriteNewLine();
        }
    }

    private static void AddPropertyFor(IVisitorContext context, ParameterSyntax parameter, string typeDefinitionVariable, INamedTypeSymbol declaringType)
    {
        using var _ = LineInformationTracker.Track(context, parameter);

        var declaringTypeVariable = context.DefinitionVariables.GetLastOf(VariableMemberKind.Type);
        if (!declaringTypeVariable.IsValid)
            throw new InvalidOperationException();
        
        context.WriteComment($"Property: {parameter.Identifier.Text} (primary constructor)");
        var propDefVar = context.Naming.SyntheticVariable(parameter.Identifier.Text, ElementKind.Property);
        var paramSymbol = context.SemanticModel.GetDeclaredSymbol(parameter).EnsureNotNull<ISymbol, IParameterSymbol>();
        var exps = context.ApiDefinitionsFactory.Property(context, declaringTypeVariable.VariableName, declaringTypeVariable.MemberName,propDefVar, parameter.Identifier.Text, context.TypeResolver.ResolveAny(paramSymbol.Type));
        
        context.Generate(exps);
        context.Generate($"{typeDefinitionVariable}.Properties.Add({propDefVar});");
        context.WriteNewLine();
        context.WriteNewLine();

        var publicPropertyMethodAttributes = "MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName";
        var propertyType = context.SemanticModel.GetDeclaredSymbol(parameter).GetMemberType();
        var propertyData = new PropertyGenerationData(
                                    declaringType.OriginalDefinition.ToDisplayString(),
                                    declaringTypeVariable.VariableName,
                                    declaringType.TypeParameters.Length > 0,
                                    propDefVar,
                                    parameter.Identifier.Text,
                                    new Dictionary<string, string>
                                    {
                                        ["get"] = publicPropertyMethodAttributes,
                                        ["set"] = publicPropertyMethodAttributes
                                    },
                                    false,
                                    resolveTargetKind => context.TypeResolver.ResolveAny(propertyType, resolveTargetKind),
                                    propertyType.ToDisplayString(),
                                    Array.Empty<ParameterSpec>(),
                                    "FieldAttributes.Private",
                                    OpCodes.Stfld,
                                    OpCodes.Ldfld);
        
        PropertyGenerator propertyGenerator = new (context);
        
        AddGetter();
        AddInit();

        void AddGetter()
        {
            context.WriteComment($"{propertyData.Name} getter");
            var getMethodVar = context.Naming.SyntheticVariable($"get{propertyData.Name}", ElementKind.Method);
            // properties for primary ctor parameters cannot override base properties, so hasCovariantReturn = false and overridenMethod = null (none)
            var ilVar = context.Naming.ILProcessor($"get{propertyData.Name}");
            using (propertyGenerator.AddGetterMethodDeclaration(in propertyData, getMethodVar, false, $"get_{propertyData.Name}", null, ilVar))
            {
                context.Generate([$"var {ilVar} = {getMethodVar}.Body.GetILProcessor();"]);
                
                propertyGenerator.AddAutoGetterMethodImplementation(in propertyData, ilVar, getMethodVar);
            }
            context.WriteNewLine();
        }
        
        void AddInit()
        {
            context.WriteComment($"{propertyData.Name} init");
            var setMethodVar = context.Naming.SyntheticVariable($"set{propertyData.Name}", ElementKind.Method);
            var ilContext = context.ApiDriver.NewIlContext(context, $"set{propertyData.Name}", setMethodVar);
            using (propertyGenerator.AddSetterMethodDeclaration(in propertyData, setMethodVar, true, $"set_{propertyData.Name}", null, ilContext))
            {
                propertyGenerator.AddAutoSetterMethodImplementation(in propertyData, ilContext);
                context.ApiDriver.WriteCilInstruction(context, ilContext, OpCodes.Ret);
            }
            context.WriteNewLine();
        }
    }

    internal static void AddPrimaryConstructor(IVisitorContext context, string recordTypeDefinitionVariable, TypeDeclarationSyntax typeDeclaration)
    {
        context.WriteComment($"Constructor: {typeDeclaration.Identifier.ValueText}{typeDeclaration.ParameterList}");
        var typeSymbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration).EnsureNotNull<ISymbol, ITypeSymbol>();
        
        var ctorVar = context.Naming.Constructor(typeDeclaration, false);
        string typeName = typeSymbol.OriginalDefinition.ToDisplayString();
        string[] paramTypes = typeDeclaration.ParameterList?.Parameters.Select(p => p.Type?.ToString()).ToArray() ?? [];
        var exps = context.ApiDefinitionsFactory.Constructor(
            context, 
            new BodiedMemberDefinitionContext("ctor", ctorVar, recordTypeDefinitionVariable, MemberOptions.None, IlContext.None), 
            typeName, 
            false, 
            "MethodAttributes.Public", 
            paramTypes, 
            null);
        var ctorExp = exps;
        context.Generate(ctorExp);

        // parameterless primary constructors still have a body comprised of a single return.
        // in this scenario Parameters will be an empty list.
        if (typeDeclaration.ParameterList?.Parameters == null)
            return;
        
        var ilContext = context.ApiDriver.NewIlContext(context, $"ctor_{typeDeclaration.Identifier.ValueText}", ctorVar);
        var ctorExps = context.ApiDefinitionsFactory.MethodBody(context, $"ctor_{typeDeclaration.Identifier.ValueText}", ilContext, [], []);
        context.Generate(ctorExps);

        var resolvedType = context.TypeResolver.ResolveAny(typeSymbol);
        Func<string, string> fieldRefResolver = backingFieldVar => typeDeclaration.TypeParameterList?.Parameters.Count > 0 
            ? $"new FieldReference({backingFieldVar}.Name, {backingFieldVar}.FieldType, {resolvedType})" 
            : backingFieldVar;

        var uniqueParameters = typeDeclaration.GetUniqueParameters(context).ToHashSet();
        foreach (var parameter in typeDeclaration.ParameterList.Parameters)
        {
            context.WriteComment($"Parameter: {parameter.Identifier}");
            var paramVar = context.Naming.Parameter(parameter);
            var parameterType = context.TypeResolver.ResolveAny(ModelExtensions.GetTypeInfo(context.SemanticModel, parameter.Type!).Type);
            var paramExps = CecilDefinitionsFactory.Parameter(parameter.Identifier.ValueText, RefKind.None, null, ctorVar, paramVar, parameterType, Constants.ParameterAttributes.None, ("", false));
            context.Generate(paramExps);

            if (!uniqueParameters.Contains(parameter))
                continue;
            
            context.ApiDriver.WriteCilInstruction(context, ilContext.VariableName, OpCodes.Ldarg_0);
            context.ApiDriver.WriteCilInstruction(context, ilContext.VariableName, OpCodes.Ldarg, paramVar);

            var backingFieldVar = context.DefinitionVariables.GetVariable(Utils.BackingFieldNameForAutoProperty(parameter.Identifier.ValueText), VariableMemberKind.Field, typeSymbol.OriginalDefinition.ToDisplayString());
            if (!backingFieldVar.IsValid)
                throw new InvalidOperationException($"Backing field variable for property '{parameter.Identifier.ValueText}' could not be found.");

            context.ApiDriver.WriteCilInstruction(context, ilContext.VariableName, OpCodes.Stfld, fieldRefResolver(backingFieldVar.VariableName));
        }

        if (!typeSymbol.IsValueType)
            InvokeBaseConstructor(context, ilContext.VariableName, typeDeclaration);
        context.ApiDriver.WriteCilInstruction(context, ilContext.VariableName, OpCodes.Ret);

        static void InvokeBaseConstructor(IVisitorContext context, string ctorIlVar, TypeDeclarationSyntax typeDeclaration)
        {
            string baseCtor;
            context.ApiDriver.WriteCilInstruction(context, ctorIlVar, OpCodes.Ldarg_0);
        
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
        
            context.ApiDriver.WriteCilInstruction(context, ctorIlVar, OpCodes.Call, baseCtor);
        }
    }
}
