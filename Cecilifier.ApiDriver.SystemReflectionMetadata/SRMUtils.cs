using System.Runtime.InteropServices;
using Cecilifier.Core.AST;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Naming;
using Cecilifier.Core.TypeSystem;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.ApiDriver.SystemReflectionMetadata;

internal struct SRMUtils
{
    internal static void AddTypeParameters(IVisitorContext ctx, IEnumerable<TypeParameterSyntax> typeParameters, string entityHandleVariable)
    {
        var index = 0;
        foreach (var genericTypeParameter in typeParameters)
        {
            var typeParameterSymbol = ctx.SemanticModel.GetDeclaredSymbol(genericTypeParameter).EnsureNotNull<ISymbol, ITypeParameterSymbol>();
            var typeParameterVarName = ctx.Naming.SyntheticVariable(typeParameterSymbol.Name, ElementKind.GenericParameter);
            
            // We only need to assign the newly constructed generic type parameter if we need to set a `type` constraint 
            var varAssignment = typeParameterSymbol.ConstraintTypes.Length > 0 || typeParameterSymbol.HasValueTypeConstraint || typeParameterSymbol.HasUnmanagedTypeConstraint 
                ? $"var {typeParameterVarName} = "
                : string.Empty; 
                
            ctx.Generate($"""{varAssignment}metadata.AddGenericParameter({entityHandleVariable}, {GenericParameterAttributesFor(typeParameterSymbol)}, metadata.GetOrAddString("{genericTypeParameter.Identifier.Text}"), {index++});""");
            foreach (var constraint in typeParameterSymbol.ConstraintTypes)
            {
                ctx.WriteNewLine();
                var resolvedType = ctx.TypeResolver.Resolve(constraint, ResolveTargetKind.GenericTypeParameterConstraint);
                ctx.Generate(StringExtensions.Indented($"metadata.AddGenericParameterConstraint({typeParameterVarName}, {resolvedType});"));
            }

            ctx.WriteNewLine();

            if (typeParameterSymbol.HasUnmanagedTypeConstraint)
            {
                ctx.Generate(StringExtensions.Indented($$"""
                                                                              {
                                                                                  var typeSpecificationSig = new BlobEncoder(new BlobBuilder()).TypeSpecificationSignature();
                                                                                  typeSpecificationSig.CustomModifiers().AddModifier({{ctx.TypeResolver.Resolve(ctx.RoslynTypeSystem.ForType<UnmanagedType>(), ResolveTargetKind.TypeReference)}}, isOptional: false);
                                                                                  typeSpecificationSig.Type({{ctx.TypeResolver.Bcl.System.ValueType}}, isValueType: true);
                                                                                  metadata.AddGenericParameterConstraint({{typeParameterVarName}}, metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpecificationSig.Builder)));
                                                                              }
                                                                              """));
                ctx.WriteNewLine();
            } 
            else if (typeParameterSymbol.HasValueTypeConstraint)
            {
                ctx.Generate(StringExtensions.Indented($"metadata.AddGenericParameterConstraint({typeParameterVarName}, {ctx.TypeResolver.Bcl.System.ValueType});"));
                ctx.WriteNewLine();
            }
        }
        static string GenericParameterAttributesFor(ITypeParameterSymbol typeParameterSymbol)
        {
            Span<char> span = stackalloc char[1024];
            Span<char> target = span;
            if (typeParameterSymbol.HasReferenceTypeConstraint)
                target = target.AppendEnumFlag("GenericParameterAttributes.ReferenceTypeConstraint");
            
            if (typeParameterSymbol.HasConstructorConstraint || typeParameterSymbol.HasValueTypeConstraint)
                target = target.AppendEnumFlag("GenericParameterAttributes.DefaultConstructorConstraint", target.Length == span.Length);

            if (typeParameterSymbol.HasValueTypeConstraint)
                target = target.AppendEnumFlag("GenericParameterAttributes.NotNullableValueTypeConstraint", target.Length == span.Length);
            
            if (typeParameterSymbol.Variance == VarianceKind.In)
                target = target.AppendEnumFlag("GenericParameterAttributes.Contravariant", target.Length == span.Length);
            
            if (typeParameterSymbol.Variance == VarianceKind.Out)
                target = target.AppendEnumFlag("GenericParameterAttributes.Covariant", target.Length == span.Length);
            
            if (span.Length != target.Length)
                return span.Slice(0, span.Length - target.Length).ToString();
            
            return "GenericParameterAttributes.None";
        }
    }
    
    // Assumes type parameters:
    // - Have no constraints (GenericParameterAttributes.None)
    // - Have no attributes (GenericParameterAttributes.None)
    internal static void AddTypeParameters(IVisitorContext ctx, IEnumerable<string> typeParameterNames, string entityHandleVariable)
    {
        var index = 0;
        foreach (var typeParameterName in typeParameterNames)
        {
            var varAssignment = string.Empty; 
                
            ctx.Generate($"""{varAssignment}metadata.AddGenericParameter({entityHandleVariable}, GenericParameterAttributes.None, metadata.GetOrAddString("{typeParameterName}"), {index++});""");
            ctx.WriteNewLine();
        }
    }    
}
