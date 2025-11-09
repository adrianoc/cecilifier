#nullable enable
using System;
using System.Collections.Generic;
using Cecilifier.Core.ApiDriver.Attributes;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Cecilifier.Core.TypeSystem;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.ApiDriver.DefinitionsFactory;

public interface IApiDriverDefinitionsFactory
{
    public string MappedTypeModifiersFor(INamedTypeSymbol type, SyntaxTokenList modifiers);

    /// <summary>Generates the code for a type declaration.</summary>
    /// <remarks>
    /// 1. At IL level, type parameters from *outer* types are considered to be part of a inner type whence these type parameters need to be added to the list of type parameters even
    ///    if the type being declared is not a generic type.
    /// 
    /// 2. Only type parameters owned by the type being declared are considered when computing the arity of the type (whence the number following the backtick reflects only the
    ///    # of the type parameters declared by the type being declared).
    /// </remarks>
    /// <param name="context"></param>
    /// <param name="definitionContext"></param>
    /// <param name="typeNamespace"></param>
    /// <param name="attrs"></param>
    /// <param name="baseType"></param>
    /// <param name="isStructWithNoFields"></param>
    /// <param name="interfaces"></param>
    /// <param name="ownTypeParameters"></param>
    /// <param name="outerTypeParameters"></param>
    /// <param name="properties"></param>
    /// <returns></returns>
    public IEnumerable<string> Type(
        IVisitorContext context,
        MemberDefinitionContext definitionContext,
        string typeNamespace,
        string attrs,
        ResolvedType baseType,
        bool isStructWithNoFields,
        IEnumerable<ITypeSymbol> interfaces,
        IEnumerable<TypeParameterSyntax>? ownTypeParameters,
        IEnumerable<TypeParameterSyntax> outerTypeParameters,
        params TypeLayoutProperty[] properties);

    public IEnumerable<string> Method(IVisitorContext context, IMethodSymbol methodSymbol, BodiedMemberDefinitionContext bodiedMemberDefinitionContext, string methodName, string methodModifiers,
        IParameterSymbol[] resolvedParameterTypes, IList<TypeParameterSyntax> typeParameters);

    public IEnumerable<string> Method(IVisitorContext context,
        BodiedMemberDefinitionContext definitionContext,
        string declaringTypeName,
        string methodModifiers, //TODO: Try to change to MethodAttributes enum (reflection) in the same way we do with IL opcodes (and remove MemberDefinitionContext.Options)
        IReadOnlyList<ParameterSpec> parameters,
        IList<string> typeParameters,
        Func<IVisitorContext, ResolvedType> returnTypeResolver,
        out MethodDefinitionVariable methodDefinitionVariable // we can't use the method name in some scenarios (indexers, for instance) 
    );

    public IEnumerable<string> Constructor(IVisitorContext context, BodiedMemberDefinitionContext definitionContext, string typeName, bool isStatic, string methodAccessibility, string[] paramTypes, string? methodDefinitionPropertyValues = null);
    public IEnumerable<string> Field(IVisitorContext context, in MemberDefinitionContext definitionContext, ISymbol fieldOrEvent, ITypeSymbol fieldType, string fieldAttributes, bool isVolatile, bool isByRef, in FieldInitializationData initializer = default);
    public IEnumerable<string> Field(IVisitorContext context, in MemberDefinitionContext definitionContext, string declaringTypeName, ResolvedType fieldType, string fieldAttributes, bool isVolatile, bool isByRef, in FieldInitializationData initializer = default);
    IEnumerable<string> MethodBody(IVisitorContext context, string methodName, IlContext ilContext, ResolvedType[] localVariableTypes, InstructionRepresentation[] instructions);
    DefinitionVariable LocalVariable(IVisitorContext context, string variableName, string methodDefinitionVariableName, ResolvedType resolvedType);
    IEnumerable<string> Property(IVisitorContext context, BodiedMemberDefinitionContext definitionContext, string declaringTypeName, List<ParameterSpec> propertyParameters, ResolvedType propertyType);
    IEnumerable<string> Attribute(IVisitorContext context, IMethodSymbol attributeCtor, string attributeVarBaseName, string attributeTargetVar, VariableMemberKind targetKind, params CustomAttributeArgument[] arguments);
}
