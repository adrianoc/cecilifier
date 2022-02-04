using System;
using System.Runtime.CompilerServices;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;

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
        CallerArgumentExpressionAttibute = ctx.SemanticModel.Compilation.GetTypeByMetadataName(typeof(CallerArgumentExpressionAttribute).FullName); 

        SystemInt32 = ctx.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
        SystemInt64 = ctx.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Int64);
        SystemIntPtr = ctx.SemanticModel.Compilation.GetSpecialType(SpecialType.System_IntPtr);
        SystemSingle = ctx.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Single);
        SystemString = ctx.SemanticModel.Compilation.GetSpecialType(SpecialType.System_String);
        SystemVoid = ctx.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Void);
        SystemObject = ctx.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Object);
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
    public ITypeSymbol CallerArgumentExpressionAttibute { get; }
}
