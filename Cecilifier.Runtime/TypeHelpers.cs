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
                var hasOpenGenericTypes = false;

                for (var i = 0; i < parameters.Length && found; i++)
                {
                    found &= CompareParameters(parameters[i], paramTypesArray[i]); 
                    hasOpenGenericTypes |= paramTypesArray[i].IsTypeParameter;
                }

                if (found)
                {
                    return hasOpenGenericTypes 
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

    /// <summary>
    /// This class is used to fix the references to System.Private.Corlib.dll. Assemblies should never reference these types; instead, they should
    /// reference types from the respective reference assembly (e.g. System.Runtime.dll, System.Runtime.Extensions.dll, etc).
    /// 
    /// The source generator (Cecilifier.TypeMapGenerator) generates a map of types to the respective reference assembly. This class uses this map to
    /// fix the references to System.Private.Corlib.dll. We only observe references to such types because we are using reflection to resolve types and methods.
    /// 
    /// This approach was chosen to try to make the cecilified code simpler/more readable. It is theoretically possible to remove the need for reflection by:
    /// 
    ///     1. Changing Cecilifier to configure Roslyn to use the reference assemblies instead of the runtime ones (same set of assemblies the code
    ///        generator uses).
    ///     2. Constructing TypeReference/MethodReference instances (based on ISymbol from Roslyn) instead of using reflection.
    /// </summary>
    internal partial class PrivateCorlibFixerMixin
    {
        // This method is implemented by the source generator (Cecilifier.TypeMapGenerator)
        partial void InitializeTypeToAssemblyNameReferenceMap();
        
        // This is initialized in the generated code.
        private static Dictionary<string, AssemblyNameReference> _typeToAssemblyNameReference = new();

        private Func<AssemblyNameReference, AssemblyNameReference> _addReferenceIfNotPresent;

        public PrivateCorlibFixerMixin(ModuleDefinition module)
        {
            if (_typeToAssemblyNameReference.Count == 0)
            {
                InitializeTypeToAssemblyNameReferenceMap();
            }
            
            _addReferenceIfNotPresent = referenceToBeAdded =>
            {
                var found = module.AssemblyReferences.FirstOrDefault(ar => ar.FullName == referenceToBeAdded.FullName);
                if (found == null)
                {
                    found = referenceToBeAdded;
                    module.AssemblyReferences.Add(referenceToBeAdded);
                }

                return found;
            };
        }
        
        public bool TryMapAssemblyFromType(string typeName, [NotNullWhen(true)] out AssemblyNameReference mappedAssemblyReference)
        {
            mappedAssemblyReference = null;

            if (_typeToAssemblyNameReference.TryGetValue(typeName, out var found))
            {
                mappedAssemblyReference =  _addReferenceIfNotPresent(found);
                return true;
            }

            Console.WriteLine($"Fail to map '{typeName}' ({typeName.GetHashCode()})");
            return false;
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

        protected override IMetadataScope ImportScope(TypeReference type)
        {
            if (importerMixin.TryMapAssemblyFromType(type.FullName, out var mappedAssemblyReference))
                return mappedAssemblyReference;
            
            return base.ImportScope(type);
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

        protected override IMetadataScope ImportScope(Type type)
        {
            if (importerMixin.TryMapAssemblyFromType(type.FullName, out var fixedn))
                return fixedn;
            
            return base.ImportScope(type);
        }
    }

    public struct ParamData
    {
        public string FullName { get; set; }
        public bool IsTypeParameter { get; set; }
        public bool IsArray { get; set; }
    }
}
