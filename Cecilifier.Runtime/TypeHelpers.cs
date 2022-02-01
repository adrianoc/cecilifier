using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using CustomAttributeNamedArgument = Mono.Cecil.CustomAttributeNamedArgument;
using ICustomAttributeProvider = Mono.Cecil.ICustomAttributeProvider;

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
       

        public static MethodInfo ResolveGenericMethod(string declaringTypeName, string methodName, BindingFlags bindingFlags, IEnumerable<string> typeArguments, IEnumerable<ParamData> paramTypes)
        {
            var declaringType = Type.GetType(declaringTypeName);
            
            var typeArgumentsCount = typeArguments.Count();
            var methods = declaringType.GetMethods(bindingFlags)
                .Where(c => c.Name == methodName
                            && c.IsGenericMethodDefinition
                            && c.GetParameters().Length == paramTypes.Count()
                            && typeArgumentsCount == c.GetGenericArguments().Length);

            if (methods == null)
            {
                throw new MissingMethodException(declaringTypeName, methodName);
            }

            var paramTypesArray = paramTypes.ToArray();
            foreach (var mc in methods)
            {
                var parameters = mc.GetParameters();
                var found = true;

                for (var i = 0; i < parameters.Length; i++)
                {
                    if (!CompareParameters(parameters[i], paramTypesArray[i]))
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                {
                    return mc.MakeGenericMethod(typeArguments.Select(Type.GetType).ToArray());
                }
            }

            return null;
        }

        public static MethodBase ResolveMethod(string declaringTypeName, string methodName, BindingFlags bindingFlags, string typeArgumentList, params string[] paramTypes)
        {
            var declaringType = Type.GetType(declaringTypeName);
            if (declaringType.IsGenericType)
            {
                var typeArguments = typeArgumentList.Split(',');
                declaringType = declaringType.MakeGenericType(typeArguments.Select(Type.GetType).ToArray());
            }

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
        
        public static MethodBase ResolveCtor(string assemblyName, string declaringTypeName, BindingFlags bindingFlags, string typeArgumentList, params string[] paramTypes)
        {
            var containingAssembly = Assembly.Load(new AssemblyName(assemblyName));
            var declaringType = containingAssembly.GetType(declaringTypeName);

            if (declaringType.IsGenericType)
            {
                var typeArguments = typeArgumentList.Split(',');
                declaringType = declaringType.MakeGenericType(typeArguments.Select(Type.GetType).ToArray());
            }

            var foundCtor = declaringType.GetConstructor(
                bindingFlags,
                null,
                paramTypes.Select(Type.GetType).ToArray(),
                null);

            return foundCtor;
        }
       
        public static MethodInfo ResolveMethod(string assemblyName, string declaringTypeName, string methodName)
        {
            var containingAssembly = Assembly.Load(new AssemblyName(assemblyName));
            var declaringType = containingAssembly.GetType(declaringTypeName);
            if (declaringType == null)
            {
                throw new InvalidOperationException($"Failed to resolve type [{assemblyName}] {declaringTypeName}");
            }

            var resolvedMethod = declaringType.GetMethod(methodName);
            if (resolvedMethod == null)
            {
                throw new InvalidOperationException($"Failed to resolve method [{assemblyName}] {declaringTypeName}.{methodName}(?)");
            }
            
            return resolvedMethod;
        }

        public static Type ResolveType(string assemblyName, string typeName)
        {
            var containingAssembly = Assembly.Load(new AssemblyName(assemblyName));
            return containingAssembly.GetType(typeName);
        }

        public static Type ResolveParameter(string assemblyName, string typeName)
        {
            var containingAssembly = Assembly.Load(new AssemblyName(assemblyName));
            return containingAssembly.GetType(typeName);
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

        private static bool CompareParameters(ParameterInfo candidate, ParamData original)
        {
            if (candidate.ParameterType.IsArray ^ original.IsArray)
            {
                return false;
            }

            var candidateElementType = candidate.ParameterType.HasElementType ? candidate.ParameterType.GetElementType() : candidate.ParameterType;
            if (candidateElementType.IsGenericParameter ^ original.IsTypeParameter)
            {
                return false;
            }

            if (original.IsTypeParameter)
            {
                return candidateElementType.Name == original.FullName;
            }

            return candidateElementType.FullName == original.FullName;
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
        private const string SystemPrivateCoreLib = "System.Private.CoreLib";
        private AssemblyNameReference _correctCorlib;

        public SystemPrivateCoreLibFixerReflectionImporter(ModuleDefinition module) : base(module)
        {
            _correctCorlib = AssemblyNameReference.Parse("netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51");
            if (!module.AssemblyReferences.Contains(_correctCorlib))
                module.AssemblyReferences.Add(_correctCorlib);

        }

        public override AssemblyNameReference ImportReference(System.Reflection.AssemblyName reference)
        {
            if (_correctCorlib != null && reference.Name == SystemPrivateCoreLib)
            {
                return _correctCorlib;
            }

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
