using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.TypeSystem
{
    public interface ITypeResolver
    {
        string ResolveAny(ITypeSymbol type, ResolveTargetKind targetKind = ResolveTargetKind.None, string cecilTypeParameterProviderVar = null);
        string ResolvePredefinedType(ITypeSymbol type);
        string ResolveLocalVariableType(ITypeSymbol type);
        string Resolve(string typeName);
        string Resolve(ITypeSymbol type);
        string MakeArrayType(ITypeSymbol elementType);

        Bcl Bcl { get; }
    }
    
    public enum ResolveTargetKind
    {
        None,
        ArrayElementType,
        Field, // Any enum values equals to or smaller than `Field` have special handling when resolving types.

        LocalVariable,
        Parameter,
        ReturnType,
    }
}
