#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cecilifier.Core.AST;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.ApiDriver;

public interface IApiDriverDefinitionsFactory
{
    public string MappedTypeModifiersFor(INamedTypeSymbol type, SyntaxTokenList modifiers);

    public IEnumerable<string> Type(
        IVisitorContext context,
        string typeVar,
        string typeNamespace,
        string typeName,
        string attrs,
        string resolvedBaseType,
        DefinitionVariable outerTypeVariable,
        bool isStructWithNoFields,
        IEnumerable<ITypeSymbol> interfaces,
        IEnumerable<TypeParameterSyntax>? ownTypeParameters,
        IEnumerable<TypeParameterSyntax> outerTypeParameters,
        params string[] properties);

    public IEnumerable<string> Method(IVisitorContext context, IMethodSymbol methodSymbol, MemberDefinitionContext memberDefinitionContext, string methodName, string methodModifiers,
        IParameterSymbol[] resolvedParameterTypes, IList<TypeParameterSyntax> typeParameters);

    public IEnumerable<string> Method(IVisitorContext context,
        MemberDefinitionContext memberDefinitionContext,
        string declaringTypeName,
        string methodNameForVariableRegistration, // we can't use the method name in some scenarios (indexers, for instance) 
        string methodName,
        string methodModifiers,
        IReadOnlyList<ParameterSpec> parameters,
        IList<string> typeParameters,
        ITypeSymbol returnType
    );

    public IEnumerable<string> Constructor(IVisitorContext context, MemberDefinitionContext memberDefinitionContext, string typeName, bool isStatic, string methodAccessibility, string[] paramTypes, string? methodDefinitionPropertyValues = null);
    public IEnumerable<string> Field(IVisitorContext context, in MemberDefinitionContext memberDefinitionContext, ISymbol fieldOrEvent, ITypeSymbol fieldType, string fieldAttributes, bool isVolatile, bool isByRef, object? constantValue = null);
    public IEnumerable<string> Field(IVisitorContext context, in MemberDefinitionContext memberDefinitionContext, string fieldVar, string name, string fieldType, string fieldAttributes, bool isVolatile, bool isByRef, object? constantValue = null);
    IEnumerable<string> MethodBody(IVisitorContext context, string methodName, IlContext ilContext, string[] localVariableTypes, InstructionRepresentation[] instructions);
    DefinitionVariable LocalVariable(IVisitorContext context, string variableName, string methodDefinitionVariableName, string resolvedVarType);
}

[DebuggerDisplay("MemberDefinitionContext ({MemberDefinitionVariableName}, {ParentDefinitionVariableName}, {IlContext})")]
public record struct MemberDefinitionContext(
    string MemberDefinitionVariableName,
    string ParentDefinitionVariableName,
    IlContext IlContext);
