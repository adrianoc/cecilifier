using System.Collections.Generic;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.TypeSystem
{
    public class SystemTypeSystem
    {
        public SystemTypeSystem(ITypeResolver typeResolver, IVisitorContext context)
        {
            _resolvedTypes = new Dictionary<SpecialType, ResolvedType>
            {
                [SpecialType.System_Int32] = typeResolver.ResolvePredefinedType(context.RoslynTypeSystem.SystemInt32),
                [SpecialType.System_Int64] = typeResolver.ResolvePredefinedType(context.RoslynTypeSystem.SystemInt64),
                [SpecialType.System_IntPtr] = typeResolver.ResolvePredefinedType(context.RoslynTypeSystem.SystemIntPtr),
                [SpecialType.System_String] = typeResolver.ResolvePredefinedType(context.RoslynTypeSystem.SystemString),
                [SpecialType.System_Void] = typeResolver.ResolvePredefinedType(context.RoslynTypeSystem.SystemVoid),
                [SpecialType.System_Object] = typeResolver.ResolvePredefinedType(context.RoslynTypeSystem.SystemObject),
                [SpecialType.System_Boolean] = typeResolver.ResolvePredefinedType(context.RoslynTypeSystem.SystemBoolean),
                [SpecialType.System_Enum] = typeResolver.ResolveAny(context.RoslynTypeSystem.ForType<System.Enum>()),
                [SpecialType.System_ValueType] = typeResolver.ResolveAny(context.RoslynTypeSystem.ForType<System.ValueType>()),
                [SpecialType.System_MulticastDelegate] = typeResolver.ResolveAny(context.RoslynTypeSystem.ForType<System.MulticastDelegate>()),
                [SpecialType.System_AsyncCallback] = typeResolver.ResolveAny(context.RoslynTypeSystem.ForType<System.AsyncCallback>()),
                [SpecialType.System_IAsyncResult] = typeResolver.ResolveAny(context.RoslynTypeSystem.ForType<System.IAsyncResult>()),
                [SpecialType.System_Nullable_T] = typeResolver.ResolveAny(context.RoslynTypeSystem.ForType<System.Nullable<int>>().OriginalDefinition),
            };
        }

        public ResolvedType Int32 => _resolvedTypes[SpecialType.System_Int32];
        public ResolvedType Int64 => _resolvedTypes[SpecialType.System_Int64];
        public ResolvedType String => _resolvedTypes[SpecialType.System_String];
        public ResolvedType Object => _resolvedTypes[SpecialType.System_Object];
        public ResolvedType Boolean => _resolvedTypes[SpecialType.System_Boolean];
        public ResolvedType IntPtr => _resolvedTypes[SpecialType.System_IntPtr];
        public ResolvedType Void => _resolvedTypes[SpecialType.System_Void];
        public ResolvedType Enum => _resolvedTypes[SpecialType.System_Enum];
        public ResolvedType MulticastDelegate => _resolvedTypes[SpecialType.System_MulticastDelegate];
        public ResolvedType AsyncCallback => _resolvedTypes[SpecialType.System_AsyncCallback];
        public ResolvedType IAsyncResult => _resolvedTypes[SpecialType.System_IAsyncResult];
        public ResolvedType NullableOfT => _resolvedTypes[SpecialType.System_Nullable_T];
        public ResolvedType ValueType => _resolvedTypes[SpecialType.System_ValueType];

        private readonly IReadOnlyDictionary<SpecialType, ResolvedType> _resolvedTypes;
    }
}
