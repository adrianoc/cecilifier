#nullable enable

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cecilifier.Core.ApiDriver;
using Cecilifier.Core.ApiDriver.Attributes;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;

namespace Cecilifier.Core.CodeGeneration;

internal class InlineArrayGenerator
{
    internal static void Reset() => _typeVariablePerElementCount.Clear();
    public static ResolvedType GetOrGenerateInlineArrayType(IVisitorContext context, int elementCount, string comment)
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
                //CecilDefinitionsFactory.GenericParameter(context, $"{typeVar}.Name", typeVar, "TElementType", typeParameterVar),
                $"""{typeVar}.GenericParameters.Add({typeParameterVar});"""
            ];
            context.Generate(typeExps);

            var fieldVar = context.Naming.SyntheticVariable("_element0", ElementKind.Field);
            string declaringTypeName = $"{typeVar}.Name";
            
            var fieldExps = context.ApiDefinitionsFactory.Field(context, new MemberDefinitionContext("_element0", fieldVar, typeVar), declaringTypeName, typeParameterVar, "FieldAttributes.Private", false, false);
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
            context.Generate($"assembly.MainModule.Types.Add({typeVar});\n");
        }
        
        return new ResolvedType(typeVar);
    }

    private static Dictionary<int, string> _typeVariablePerElementCount = new();
}
