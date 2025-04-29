#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.CodeGeneration;

internal class InlineArrayGenerator
{
    internal static void Reset() => _typeVariablePerElementCount.Clear();
    public static string GetOrGenerateInlineArrayType(IVisitorContext context, int elementCount, string comment)
    {
        ref var typeVar = ref CollectionsMarshal.GetValueRefOrAddDefault(_typeVariablePerElementCount, elementCount, out var exists)!;
        if (!exists)
        {
            context.WriteNewLine();
            context.WriteComment(comment);
            
            typeVar = context.Naming.Type("inlineArray", ElementKind.Struct);
            var typeParameterVar = context.Naming.SyntheticVariable("TElementType", ElementKind.GenericParameter);

            string[] typeExps = 
            [
                //[StructLayout(LayoutKind.Auto)]
                $"""var {typeVar} = new TypeDefinition(string.Empty, "<>y_InlineArray{elementCount}`1", TypeAttributes.NotPublic | TypeAttributes.AutoLayout | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit | TypeAttributes.Sealed, {context.TypeResolver.Bcl.System.ValueType});""",
                CecilDefinitionsFactory.GenericParameter(context, $"{typeVar}.Name", typeVar, "TElementType", typeParameterVar),
                $"""{typeVar}.GenericParameters.Add({typeParameterVar});"""
            ];

            var fieldVar = context.Naming.SyntheticVariable("_element0", ElementKind.Field);
            var fieldExps = CecilDefinitionsFactory.Field(
                context, 
                $"{typeVar}.Name", 
                typeVar,
                fieldVar, 
                "_element0", 
                typeParameterVar, 
                "FieldAttributes.Private");
            
            context.WriteCecilExpressions(typeExps);
            
            //[InlineArray(2)]
            context.WriteCecilExpressions(
                CecilDefinitionsFactory.Attribute(
                    "inlineArray", 
                    typeVar, 
                    context,
                    ConstructorFor<InlineArrayAttribute>(context, typeof(int)),
                    (context.TypeResolver.Bcl.System.Int32, elementCount.ToString())));
            
            context.WriteCecilExpressions(fieldExps);
            context.AddCompilerGeneratedAttributeTo(fieldVar);
            context.WriteCecilExpression($"assembly.MainModule.Types.Add({typeVar});\n");
        }
        
        return typeVar;
    }
    
    private static string ConstructorFor<TType>(IVisitorContext context, params Type[] ctorParamTypes)
    {
        var typeSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(TType).FullName!).EnsureNotNull();
        var ctors = typeSymbol.Constructors.Where(ctor => ctor.Parameters.Length == ctorParamTypes.Length);

        if (ctors.Count() == 1)
            return ctors.First().MethodResolverExpression(context);

        var expectedParamTypes = ctorParamTypes.Select(paramType => context.SemanticModel.Compilation.GetTypeByMetadataName(paramType.FullName!)).ToHashSet(SymbolEqualityComparer.Default);
        return ctors.Single(ctor => !ctor.Parameters.Select(p => p.Type).ToHashSet(SymbolEqualityComparer.Default).Except(expectedParamTypes, SymbolEqualityComparer.Default).Any()).MethodResolverExpression(context);
    }
    
    private static Dictionary<int, string> _typeVariablePerElementCount = new();
}
