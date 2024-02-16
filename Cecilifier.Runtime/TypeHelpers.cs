using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Mono.Cecil;

namespace Cecilifier.Runtime
{
    public class TypeHelpers
    {
        public static MethodReference DefaultCtorFor(TypeReference type)
        {
            var resolved = type.Resolve();
            if (resolved == null)
                return null;

            var ctor = resolved.Methods.SingleOrDefault(m => m.IsConstructor && m.Parameters.Count == 0 && !m.IsStatic);
            if (ctor == null)
                return DefaultCtorFor(resolved.BaseType);

            return new MethodReference(".ctor", type.Module.TypeSystem.Void, type) { HasThis = true };
        }

        public static MethodBase ResolveMethod(Type declaringType, string methodName, BindingFlags bindingFlags, params string[] paramTypes)
        {
            if (methodName == ".ctor")
            {
                var resolvedCtor = declaringType.GetConstructor(
                    bindingFlags,
                    null,
                    paramTypes.Select(Type.GetType).ToArray(),
                    null);

                if (resolvedCtor == null)
                {
                    throw new InvalidOperationException($"Failed to resolve ctor [{declaringType}({string.Join(',', paramTypes)})");
                }

                return resolvedCtor;
            }

            var resolvedMethod = declaringType.GetMethod(methodName,
                bindingFlags,
                null,
                paramTypes.Select(Type.GetType).ToArray(),
                null);

            if (resolvedMethod == null)
            {
                throw new InvalidOperationException($"Failed to resolve method {declaringType}.{methodName}({string.Join(',', paramTypes)})");
            }

            return resolvedMethod;
        }
        
        public static MethodInfo ResolveGenericMethodInstance(string declaringTypeName, string methodName, BindingFlags bindingFlags, IEnumerable<ParamData> paramTypes, IEnumerable<string> genericParameters)
        {
            var declaringType = Type.GetType(declaringTypeName);

            var numberOfGenericParameters = genericParameters.Count();
            var methods = declaringType.GetMethods(bindingFlags)
                .Where(c => c.Name == methodName
                            && c.IsGenericMethodDefinition
                            && c.GetParameters().Length == paramTypes.Count()
                            && numberOfGenericParameters == c.GetGenericArguments().Length)
                .ToArray();

            if (methods.Length == 0)
            {
                throw new MissingMethodException(declaringTypeName, methodName);
            }

            var paramTypesArray = paramTypes.ToArray();
            foreach (var mc in methods)
            {
                var parameters = mc.GetParameters();
                var found = true;
                var x = false;

                for (var i = 0; i < parameters.Length; i++)
                {
                    if (paramTypesArray[i].IsTypeParameter)
                        x = true;
                    
                    if (!CompareParameters(parameters[i], paramTypesArray[i]))
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                {
                    return x 
                        ? mc 
                        : mc.MakeGenericMethod(genericParameters.Select(Type.GetType).ToArray());
                }
            }

            return null;
        }

        private static bool CompareParameters(ParameterInfo candidate, ParamData original)
        {
            if (candidate.ParameterType.IsArray ^ original.IsArray)
            {
                return false;
            }

            var candidateElementType = candidate.ParameterType.HasElementType ? candidate.ParameterType.GetElementType() : candidate.ParameterType;
            //if (candidateElementType.IsGenericParameter ^ original.IsTypeParameter)
            if (candidateElementType.IsGenericParameter)
            {
                return true;
            }

            if (original.IsTypeParameter)
            {
                return candidateElementType.Name == original.FullName;
            }

            return candidateElementType.FullName == original.FullName;
        }
        
        public static FieldInfo ResolveField(string declaringType, string fieldName)
        {
            var type = Type.GetType(declaringType);
            if (type == null)
            {
                throw new Exception("Could not resolve field: '" + fieldName + "'. Type '" + declaringType + "' not found.");
            }

            return type.GetField(fieldName);
        }
        
        public static byte[] ToByteArray<T>(Span<T> data) where T : IBinaryInteger<T>
        {
            var size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            var converted  = new byte[size * data.Length];
            Span<byte> convertedSpan = converted;
        
            int pos = 0;
            foreach(var v in data)
            {
                pos += v.WriteLittleEndian(convertedSpan.Slice(pos));
            }

            return converted;
        }
    }

    struct PrivateCorlibFixerMixin
    {
        const string SystemPrivateCoreLib = "System.Private.CoreLib";
        AssemblyNameReference _correctCorlib;

        public PrivateCorlibFixerMixin(ModuleDefinition module)
        {
            _correctCorlib = AssemblyNameReference.Parse("netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51");
            if (!module.AssemblyReferences.Contains(_correctCorlib))
                module.AssemblyReferences.Add(_correctCorlib);
        }

        internal bool TryMapAssemblyName(string candidateAssemblyName, [NotNullWhen(true)] out AssemblyNameReference correctCorlibReference)
        {
            correctCorlibReference = null;
            if (_correctCorlib == null || candidateAssemblyName != SystemPrivateCoreLib)
                return false;

            correctCorlibReference = _correctCorlib;
            return true;
        }
    }
    
    public class SystemPrivateCoreLibFixerMetadataImporterProvider : IMetadataImporterProvider
    {
        public IMetadataImporter GetMetadataImporter(ModuleDefinition module) => new SystemPrivateCoreLibFixerMetadataImporter(module);
    }

    internal class SystemPrivateCoreLibFixerMetadataImporter : DefaultMetadataImporter
    {
        private PrivateCorlibFixerMixin importerMixin;
        
        public SystemPrivateCoreLibFixerMetadataImporter(ModuleDefinition module) : base(module)
        {
            importerMixin = new PrivateCorlibFixerMixin(module);
        }

        public override AssemblyNameReference ImportReference (AssemblyNameReference name)
        {
            if (importerMixin.TryMapAssemblyName(name.Name, out var correctCorlibReference))
                return correctCorlibReference;

            return base.ImportReference(name);
        }
    }
    
    public class SystemPrivateCoreLibFixerReflectionProvider : IReflectionImporterProvider
    {
        public IReflectionImporter GetReflectionImporter(ModuleDefinition module)
        {
            return new SystemPrivateCoreLibFixerReflectionImporter(module);
        }
    }

    internal class SystemPrivateCoreLibFixerReflectionImporter : DefaultReflectionImporter
    {
        private PrivateCorlibFixerMixin importerMixin;
        
        public SystemPrivateCoreLibFixerReflectionImporter(ModuleDefinition module) : base(module)
        {
            importerMixin = new PrivateCorlibFixerMixin(module);
        }

        public override AssemblyNameReference ImportReference(AssemblyName reference)
        {
            if (importerMixin.TryMapAssemblyName(reference.Name, out var correctCorlibReference))
                return correctCorlibReference;

            return base.ImportReference(reference);
        }
    }

    public struct ParamData
    {
        public string FullName { get; set; }
        public bool IsTypeParameter { get; set; }
        public bool IsArray { get; set; }
    }
}
