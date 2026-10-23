using Cecilifier.Core.Extensions;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata.TypeSystem;

public static class TypeSystemHelpers
{
    public static DefinitionVariable NewMethodSignature(SystemReflectionMetadataContext context, IMethodSymbol method)
    {
        var methodSignatureVariableToFind = method.AsMethodDefinitionVariable(VariableMemberKind.MethodSignature);

        var methodSignatureVarName = context.Naming.SyntheticVariable($"{method.Name}Signature", ElementKind.MemberReference);
        var methodSignatureVar = FindOrRegisterVariable(context, method, methodSignatureVariableToFind, methodSignatureVarName, null!, static (ctx, method, methodSignatureVarName, _) =>
        {
            var isInstanceMethod = !method.IsStatic 
                                   && method.MethodKind != MethodKind.LocalFunction // local functions are always declared as static (we don't support capturing variables)
                                   && method.MethodKind != MethodKind.FunctionPointerSignature; // Function pointers are always static
            var methodSignatureBlobVar = ctx.Naming.SyntheticVariable($"{method.ToValidVariableName()}BlobBuilder", ElementKind.MemberReference);

            ctx.Generate($$"""
                           var {{methodSignatureBlobVar}} = new BlobBuilder();

                           new BlobEncoder({{methodSignatureBlobVar}}).
                               MethodSignature(isInstanceMethod: {{isInstanceMethod.ToKeyword()}}, genericParameterCount: {{method.TypeArguments.Length}}).
                               Parameters({{method.Parameters.Length}},
                                   returnEncoder => returnEncoder.{{ctx.TypedTypeResolver.Resolve(method.OriginalDefinition.ReturnType, method.ToTypeResolutionContext())}},
                                   parameterEncoder =>
                                   {
                                       {{
                                           string.Join('\n', method.OriginalDefinition.Parameters.Select(p => ParameterExpressionFor(ctx, p)))}}
                                   });

                           var {{methodSignatureVarName}} = metadata.GetOrAddBlob({{methodSignatureBlobVar}});
                           """);
            ctx.WriteNewLine();
        });        
        
        return methodSignatureVar;
    }
    
    public static DefinitionVariable FindOrRegisterVariable<TTypeArgumentsState>(SystemReflectionMetadataContext context, TTypeArgumentsState typeArgumentsState, MethodDefinitionVariable tbf, string variableNameToRegister, string openMethodVar, Action<SystemReflectionMetadataContext, TTypeArgumentsState, string, string> action)
    {
        var instantiationVar = context.DefinitionVariables.GetMethodVariable(tbf);
        if (!instantiationVar.IsValid)
        {
            action(context, typeArgumentsState, variableNameToRegister, openMethodVar);
            instantiationVar = context.DefinitionVariables.RegisterMethod(tbf.WithVariableName(variableNameToRegister));
        }

        return instantiationVar;
    }
    
    internal static string ParameterExpressionFor(SystemReflectionMetadataContext ctx, IParameterSymbol p)
    {
        var resolvedParameterType = ctx.TypedTypeResolver.Resolve(p.Type, new TypeResolutionContext(ResolveTargetKind.Parameter, TypeResolutionOptionsFor(p)));
        return p.HasExplicitDefaultValue 
            ? $"""metadata.AddConstant(parameterEncoder.AddParameter().{resolvedParameterType}, {p.ExplicitDefaultValue});""" 
            : $"""parameterEncoder.AddParameter().{resolvedParameterType};""";
    }
    
    private static TypeResolutionOptions TypeResolutionOptionsFor(IParameterSymbol parameter)
    {
        var byRefState= parameter.RefKind != RefKind.None 
            ? TypeResolutionOptions.IsByRef 
            : TypeResolutionOptions.None;
            
        return (parameter.Type.IsValueType ? TypeResolutionOptions.IsValueType : TypeResolutionOptions.None) | byRefState;
    }
}
