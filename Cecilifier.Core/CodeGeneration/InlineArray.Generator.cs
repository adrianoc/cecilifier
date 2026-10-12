#nullable enable

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.ApiDriver.Attributes;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.CodeGeneration;

internal class InlineArrayGenerator
{
    internal static void Reset() => _typeVariablePerElementCount.Clear();
    
    internal static (ResolvedType, string) GetOrGenerateInlineArrayType(IVisitorContext context, int elementCount, string comment)
    {
        var inlineArrayTypeName = Constants.CompilerGeneratedTypes.InlineArrayNameForLength(elementCount);
        
        ref var typeVar = ref CollectionsMarshal.GetValueRefOrAddDefault(_typeVariablePerElementCount, elementCount, out var exists)!;
        if (!exists)
        {
            context.WriteNewLine();
            context.WriteComment(comment);
            
            typeVar = context.Naming.Type("inlineArray", ElementKind.Struct);
            var memberDefinitionContext = new MemberDefinitionContext(inlineArrayTypeName, $"syntheticInlineArray{elementCount}", typeVar, null);
            var typeExps = context.ApiDefinitionsFactory.Type(
                                                        context,
                                                        memberDefinitionContext,
                                                        "", 
                                                        "TypeAttributes.NotPublic | TypeAttributes.AutoLayout | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | TypeAttributes.Sealed",
                                                        context.TypeResolver.Resolve(context.RoslynTypeSystem.SystemValueType, ResolveTargetKind.TypeReference),
                                                        false,
                                                        [],
                                                        ["TElementType"],
                                                        [],
                                                        []);
            context.Generate(typeExps);

            var fieldVar = context.Naming.SyntheticVariable("_element0", ElementKind.Field);
            var declaringTypeName = $"{typeVar}.Name";

            var typeParameterVar = context.DefinitionVariables.GetVariable("TElementType", VariableMemberKind.TypeParameter, memberDefinitionContext.Identifier);
            typeParameterVar.ThrowIfVariableIsNotValid();

            var resolvedTypeParameter = context.TypeResolver.ResolveTypeParameter(typeParameterVar.VariableName, TypeParameterKind.Type, ResolveTargetKind.TypeReference);
            
            var fieldExps = context.ApiDefinitionsFactory.Field(context, new MemberDefinitionContext("_element0", fieldVar, typeVar), declaringTypeName, resolvedTypeParameter, "FieldAttributes.Private", false, false);
            context.Generate(fieldExps);

            //[InlineArray(2)]
            var exps = context.ApiDefinitionsFactory.Attribute(
                                                context,
                                                context.RoslynTypeSystem.ForType<InlineArrayAttribute>().Ctor(context.RoslynTypeSystem.SystemInt32),
                                                "inlineArray",
                                                typeVar,
                                                VariableMemberKind.Type,
                                                new CustomAttributeArgument { Value = elementCount });
            context.Generate(exps);
            
            context.AddCompilerGeneratedAttributeTo(fieldVar, VariableMemberKind.Field);
        }
        
        return (new ResolvedType(typeVar), inlineArrayTypeName);
    }

    private static Dictionary<int, string> _typeVariablePerElementCount = new();
}
