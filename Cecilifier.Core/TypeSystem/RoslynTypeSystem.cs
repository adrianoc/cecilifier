using System;
using System.Runtime.CompilerServices;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;
using IsByRefLikeAttribute = System.Runtime.CompilerServices.IsByRefLikeAttribute;

namespace Cecilifier.Core.TypeSystem;

/// <summary>
/// RoslynTypeSystem contains Roslyn symbols for common types. These are useful when one needs to
/// compare or emit code referencing those types. 
/// </summary>
internal struct RoslynTypeSystem
{
    public RoslynTypeSystem(IVisitorContext ctx)
    {
        SystemIndex = ctx.SemanticModel.Compilation.GetTypeByMetadataName(typeof(Index).FullName);
        SystemRange = ctx.SemanticModel.Compilation.GetTypeByMetadataName(typeof(Range).FullName);
        SystemType = ctx.SemanticModel.Compilation.GetTypeByMetadataName(typeof(Type).FullName);
        SystemSpan = ctx.SemanticModel.Compilation.GetTypeByMetadataName(typeof(Span<>).FullName);
        CallerArgumentExpressionAttribute = ctx.SemanticModel.Compilation.GetTypeByMetadataName(typeof(CallerArgumentExpressionAttribute).FullName);

        SystemInt32 = ctx.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
        SystemInt64 = ctx.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Int64);
        SystemIntPtr = ctx.SemanticModel.Compilation.GetSpecialType(SpecialType.System_IntPtr);
        SystemSingle = ctx.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Single);
        SystemString = ctx.SemanticModel.Compilation.GetSpecialType(SpecialType.System_String);
        SystemVoid = ctx.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Void);
        SystemObject = ctx.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Object);
        SystemBoolean = ctx.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Boolean);
        SystemIDisposable = ctx.SemanticModel.Compilation.GetTypeByMetadataName(typeof(IDisposable).FullName);
        IsReadOnlyAttribute = ctx.SemanticModel.Compilation.GetTypeByMetadataName(typeof(IsReadOnlyAttribute).FullName);
        SystemActivator = ctx.SemanticModel.Compilation.GetTypeByMetadataName(typeof(Activator).FullName);
        IsByRefLikeAttribute = ctx.SemanticModel.Compilation.GetTypeByMetadataName(typeof(IsByRefLikeAttribute).FullName);
        SystemObsoleteAttribute = ctx.SemanticModel.Compilation.GetTypeByMetadataName(typeof(ObsoleteAttribute).FullName);
        SystemValueType = ctx.SemanticModel.Compilation.GetTypeByMetadataName(typeof(ValueType).FullName);
    }

    public ITypeSymbol SystemIndex { get; }
    public ITypeSymbol SystemRange { get; }
    public ITypeSymbol SystemType { get; }
    public ITypeSymbol SystemSpan { get; }
    public ITypeSymbol SystemInt32 { get; }
    public ITypeSymbol SystemInt64 { get; }
    public ITypeSymbol SystemIntPtr { get; }
    public ITypeSymbol SystemSingle { get; }
    public ITypeSymbol SystemString { get; }
    public ITypeSymbol SystemVoid { get; }
    public ITypeSymbol SystemObject { get; }
    public ITypeSymbol SystemBoolean { get; }
    public ITypeSymbol SystemActivator { get; }
    public ITypeSymbol SystemIDisposable { get; }
    public ITypeSymbol CallerArgumentExpressionAttribute { get; }
    public ITypeSymbol IsReadOnlyAttribute { get; }
    public ITypeSymbol IsByRefLikeAttribute { get; }
    public ITypeSymbol SystemObsoleteAttribute { get; }
    
    public ITypeSymbol SystemValueType { get; }
}
