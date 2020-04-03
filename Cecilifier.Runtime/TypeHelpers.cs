using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cecilifier.Runtime
{
    public class TypeHelpers
    {
        static AssemblyNameReference _systemRuntimeRef;

        static TypeHelpers()
        {
            var systemRuntime = AppDomain.CurrentDomain.GetAssemblies().Single(mr => mr.GetName().Name == "System.Runtime");

            // in most platforms, referencing System.Object and other types ends up adding a reference to System.Private.CoreLib (not that in these platforms, System.Runtime has type forwarders for these types).
            // To avoid this reference to System.Private.CoreLib we update these types to pretend they come from System.Runtime instead.
            _systemRuntimeRef = new AssemblyNameReference(systemRuntime.GetName().Name, systemRuntime.GetName().Version)
            {
                PublicKeyToken = new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a }
            };
        }
        
        /// <summary>Changes types referencing mscorlib so they appear to be defined in System.Runtime.dll</summary>
        /// <param name="self">type to be checked</param>
        /// <param name="mainModule">module which assembly references will be added to/removed from</param>
        /// <returns>the same type reference passed as the parameter. This allows the method to be used in chains of calls.</returns>
        public static TypeReference Fix(TypeReference self, ModuleDefinition mainModule)
        {
            if (self.DeclaringType != null)
            {
                Fix(self.DeclaringType, mainModule);
            }
            else
            {
                if (self.Scope.Name == "mscorlib")
                {
                    if (!mainModule.AssemblyReferences.Any(a => a.Name == _systemRuntimeRef.Name))
                    {
                        mainModule.AssemblyReferences.Add(_systemRuntimeRef);
                        mainModule.AssemblyReferences.Remove((AssemblyNameReference) self.Scope);
                    }
                    
                    self.Scope = _systemRuntimeRef;
                }
            }

            return self;
        }

        public static T Fix<T>(T member, ModuleDefinition mainModule) where T : MemberReference
        {
            Fix(member.DeclaringType, mainModule);
            return member;
        }
        
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
       

        public static MethodInfo ResolveGenericMethod(string assemblyName, string declaringTypeName, string methodName, BindingFlags bindingFlags, IEnumerable<string> typeArguments,
            IEnumerable<ParamData> paramTypes)
        {
            var containingAssembly = Assembly.Load(assemblyName);
            var declaringType = containingAssembly.GetType(declaringTypeName);

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

        public static MethodInfo ResolveMethod(string assemblyName, string declaringTypeName, string methodName, BindingFlags bindingFlags, string typeArgumentList, params string[] paramTypes)
        {
            var containingAssembly = Assembly.Load(new AssemblyName(assemblyName));
            var declaringType = containingAssembly.GetType(declaringTypeName);

            if (declaringType.IsGenericType)
            {
                var typeArguments = typeArgumentList.Split(',');
                declaringType = declaringType.MakeGenericType(typeArguments.Select(Type.GetType).ToArray());
            }

            var resolveMethod = declaringType.GetMethod(methodName,
                bindingFlags,
                null,
                paramTypes.Select(typeName => Type.GetType(typeName)).ToArray(),
                null);

            return resolveMethod;
        }
       
        public static MethodInfo ResolveMethod(string assemblyName, string declaringTypeName, string methodName)
        {
            var containingAssembly = Assembly.Load(new AssemblyName(assemblyName));
            var declaringType = containingAssembly.GetType(declaringTypeName);

            return declaringType.GetMethod(methodName);
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

    public struct ParamData
    {
        public string FullName { get; set; }
        public bool IsTypeParameter { get; set; }
        public bool IsArray { get; set; }
    }
}
