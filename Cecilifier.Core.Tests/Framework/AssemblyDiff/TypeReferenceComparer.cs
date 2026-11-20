using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff;

public sealed class TypeReferenceComparer : IEqualityComparer<TypeReference>
{
    public static TypeReferenceComparer Instance = new();
    private TypeReferenceComparer() { }

    public bool Equals(TypeReference x, TypeReference y)
    {
        InlineArray16<TypeReference> tempBuffer = new();
        int count = 0;
        return Equals(x, y, tempBuffer, ref count);
    }

    public bool Equals(TypeReference x, TypeReference y, Span<TypeReference> processed, ref int processedSize)
    {
        for (int i = 0; i < processedSize; i++)
        {
            if (ReferenceEquals(x, processed[i]))
                return true;
        }
        
        processed[processedSize++] = x;
        
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null)
        {
            return false;
        }

        if (y is null)
        {
            return false;
        }

        if (x is ArrayType xa && y is ArrayType ya)
        {
            if (xa.Rank != ya.Rank)
                return false;
            
            return Equals(xa.ElementType, ya.ElementType, processed, ref processedSize);
        }

        if (x is PointerType xp && y is PointerType yp)
        {
            return Equals(xp.ElementType, yp.ElementType, processed, ref processedSize);
        }

        if (x is ByReferenceType xb && y is ByReferenceType yb)
        {
            return  Equals(xb.ElementType, yb.ElementType, processed, ref processedSize);
        }
        
        // It seems Cecil may have wrong data for TypeReferences unless we resolve them.
        if (x.MetadataType != MetadataType.Var && x.MetadataType != MetadataType.MVar)
        {
            x = x.Resolve();
        }
        
        if (y.MetadataType != MetadataType.Var && y.MetadataType != MetadataType.MVar)
        {
            y = y.Resolve();
        }
        
        var equal =  
            x.IsWindowsRuntimeProjection == y.IsWindowsRuntimeProjection 
            && x.ContainsGenericParameter == y.ContainsGenericParameter 
            && x.Namespace == y.Namespace 
            && x.IsValueType == y.IsValueType 
            && x.IsNested == y.IsNested 
            && x.HasGenericParameters == y.HasGenericParameters 
            && Equals(x.DeclaringType?.Resolve(), y.DeclaringType?.Resolve(), processed, ref processedSize) 
            && x.IsByReference == y.IsByReference 
            && x.IsPointer == y.IsPointer 
            && x.IsSentinel == y.IsSentinel 
            && x.IsArray == y.IsArray 
            && x.IsGenericParameter == y.IsGenericParameter 
            && x.IsGenericInstance == y.IsGenericInstance 
            && x.IsRequiredModifier == y.IsRequiredModifier 
            && x.IsOptionalModifier == y.IsOptionalModifier 
            && x.IsPinned == y.IsPinned 
            && x.IsFunctionPointer == y.IsFunctionPointer 
            && x.IsPrimitive == y.IsPrimitive 
            && x.MetadataType == y.MetadataType;
        
        if (!equal)
            return false;
        
        for (int i = 0; i < x.GenericParameters.Count; i++)
        {
            if (!Equals(x.GenericParameters[i], y.GenericParameters[i], processed, ref processedSize))
                return false;
        }
        
        if (x is GenericParameter xg && y is GenericParameter yg && xg.Position != yg.Position)
        {
            return false;
        }

        return true;
    }
    
    public int GetHashCode(TypeReference obj)
    {
        var hashCode = new HashCode();
        hashCode.Add(obj.IsWindowsRuntimeProjection);
        hashCode.Add(obj.ContainsGenericParameter);
        hashCode.Add(obj.Namespace);
        hashCode.Add(obj.FullName);
        hashCode.Add(obj.Name);
        hashCode.Add(obj.IsValueType);
        hashCode.Add(obj.HasGenericParameters);
        hashCode.Add(obj.GenericParameters);
        hashCode.Add(obj.IsNested);
        hashCode.Add(obj.DeclaringType);
        hashCode.Add(obj.IsByReference);
        hashCode.Add(obj.IsPointer);
        hashCode.Add(obj.IsSentinel);
        hashCode.Add(obj.IsArray);
        hashCode.Add(obj.IsGenericParameter);
        hashCode.Add(obj.IsGenericInstance);
        hashCode.Add(obj.IsRequiredModifier);
        hashCode.Add(obj.IsOptionalModifier);
        hashCode.Add(obj.IsPinned);
        hashCode.Add(obj.IsFunctionPointer);
        hashCode.Add(obj.IsPrimitive);
        hashCode.Add((int)obj.MetadataType);
        return hashCode.ToHashCode();
    }
}
