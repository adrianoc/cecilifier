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

        public static MethodBase ResolveMethod(string assemblyName, string declaringTypeName, string methodName, BindingFlags bindingFlags, string typeArgumentList, params string[] paramTypes)
        {
            var containingAssembly = Assembly.Load(new AssemblyName(assemblyName));
            var declaringType = containingAssembly.GetType(declaringTypeName);

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
                    throw new InvalidOperationException($"Failed to resolve ctor [{assemblyName}] {declaringType}({string.Join(',', paramTypes)})");
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
                throw new InvalidOperationException($"Failed to resolve method [{assemblyName}] {declaringType}.{methodName}({string.Join(',', paramTypes)})");
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

    public struct PrivateCoreLibFixer
    {
        static AssemblyNameReference _systemRuntimeRef;

        static PrivateCoreLibFixer()
        {
            var systemRuntime = AppDomain.CurrentDomain.GetAssemblies().Single(mr => mr.GetName().Name == "System.Runtime");

            // in most platforms, referencing System.Object and other types ends up adding a reference to System.Private.CoreLib (note that in these platforms, System.Runtime has type forwarders for these types).
            // To avoid this reference to System.Private.CoreLib we update these types to pretend they come from System.Runtime instead.
            _systemRuntimeRef = new AssemblyNameReference(systemRuntime.GetName().Name, systemRuntime.GetName().Version)
            {
                PublicKeyToken = new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a }
            };
        }
        
        /// <summary>Changes types referencing mscorlib so they appear to be defined in System.Runtime.dll</summary>
        /// <param name="mainModule">module which assembly references will be added to/removed from</param>
        public static void FixReferences(ModuleDefinition mainModule)
        {
            foreach (var t in mainModule.GetAllTypes())
            {
                FixType(t, mainModule);
            }

            var toBeRemoved = mainModule.AssemblyReferences.Where(a => a.Name == "mscorlib" || a.Name == "System.Private.CoreLib").ToArray();
            foreach (var tbr in toBeRemoved)
            {
                mainModule.AssemblyReferences.Remove(tbr);
            }
        }
        
        private static void FixType(TypeDefinition type, ModuleDefinition mainModule)
        {
            FixTypeReferences(type.BaseType, mainModule);
            FixAttributes(type.CustomAttributes, mainModule);
            
            foreach (var field in type.Fields)
            {
                FixTypeReferences(field.FieldType.GetElementType(), mainModule);
            }

            foreach (var property in type.Properties)
            {
                FixTypeReferences(property.PropertyType.GetElementType(), mainModule);
                FixParameters(property.Parameters, mainModule);
            }
            
            foreach (var method in type.Methods)
            {
                FixTypeReferences(method, mainModule);
            }

            foreach (var @event in type.Events)
            {
                FixTypeReferences(@event.EventType.GetElementType(), mainModule);
            }
        }

        private static void FixTypeReferences(MethodReference method, ModuleDefinition mainModule)
        {
            FixTypeReferences(method.ReturnType.GetElementType(), mainModule);
            FixParameters(method.Parameters, mainModule);
            
            TryFixTypeReferencesInGenericInstance(method, mainModule);
        }

        private static void FixAttributes(Collection<CustomAttribute> customAttributes, ModuleDefinition mainModule)
        {
            foreach (var attribute in customAttributes)
            {
                FixTypeReferences(attribute.AttributeType.GetElementType(), mainModule);
                FixTypeReferences(attribute.Constructor, mainModule);
                FixTypeReferences(attribute.Fields, mainModule);
                FixTypeReferences(attribute.Properties, mainModule);
                FixTypeReferences(attribute.ConstructorArguments, mainModule);
            }
        }

        private static void FixTypeReferences(Collection<CustomAttributeArgument> attributeConstructorArguments, ModuleDefinition mainModule)
        {
            foreach (var constructorArgument in attributeConstructorArguments)
            {
                FixTypeReferences(constructorArgument, mainModule);
            }
        }

        private static void FixTypeReferences(Collection<CustomAttributeNamedArgument> attributeFields, ModuleDefinition mainModule)
        {
            foreach (var attributeField in attributeFields)
            {
                FixTypeReferences(attributeField.Argument, mainModule);
            }
        }

        private static void FixTypeReferences(CustomAttributeArgument customAttributeArgument, ModuleDefinition mainModule)
        {
            FixTypeReferences(customAttributeArgument.Type.GetElementType(), mainModule);
            if (customAttributeArgument.Value is TypeReference t) 
                FixTypeReferences(t, mainModule);
        }

        private static void FixParameters(Collection<ParameterDefinition> parameters, ModuleDefinition mainModule)
        {
            foreach (var parameter in parameters)
            {
                FixTypeReferences(parameter.ParameterType.GetElementType(), mainModule);
            }
        }

        private static void FixTypeReferences(TypeReference t, ModuleDefinition mainModule)
        {
            if (t == null) 
                return;
            
            if (t.Scope.Name == "mscorlib" || t.Scope.Name == "System.Private.CoreLib")
            {
                if (!mainModule.AssemblyReferences.Any(a => a.Name == _systemRuntimeRef.Name))
                {
                    mainModule.AssemblyReferences.Add(_systemRuntimeRef);    
                }
                  
                if (t is GenericInstanceType gt)
                {
                }
                else
                {
                    t.Scope = _systemRuntimeRef;
                }
            }


            if (t is ICustomAttributeProvider customAttributeProvider)
            {
                FixAttributes(customAttributeProvider.CustomAttributes, mainModule);
            }

            TryFixTypeReferencesInGenericInstance(t, mainModule);
        }

        private static void TryFixTypeReferencesInGenericInstance(MemberReference memberReference, ModuleDefinition mainModule)
        {
            if (memberReference is IGenericInstance gi)
            {
                foreach (var genericArgument in gi.GenericArguments)
                {
                    FixTypeReferences(genericArgument.GetElementType(), mainModule);
                }

                if (gi is GenericInstanceType git)
                {
                    foreach (var genericParameter in git.GenericParameters)
                    {
                        FixTypeReferences(genericParameter.GetElementType(), mainModule);
                        FixTypeReferences(genericParameter.Constraints, mainModule);
                    }

                    FixTypeReferences(git.ElementType, mainModule);
                }
                
            }
        }

        private static void FixTypeReferences(Collection<GenericParameterConstraint> genericConstraints, ModuleDefinition mainModule)
        {
            foreach (var genericConstraint in genericConstraints)
            {
                FixTypeReferences(genericConstraint.ConstraintType.GetElementType(), mainModule);
                FixAttributes(genericConstraint.CustomAttributes, mainModule);
            }
        }
    }

    public struct ParamData
    {
        public string FullName { get; set; }
        public bool IsTypeParameter { get; set; }
        public bool IsArray { get; set; }
    }
}
