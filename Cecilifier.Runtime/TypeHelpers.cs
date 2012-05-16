using System;
using System.Linq;
using System.Reflection;
using Mono.Cecil;

namespace Cecilifier.Runtime
{
	public class TypeHelpers
	{
		public static MethodReference DefaultCtorFor(TypeDefinition type)
		{
			var ctor = type.Methods.Where(m => m.IsConstructor && m.Parameters.Count == 0).SingleOrDefault();
			return ctor ?? DefaultCtorFor(type.BaseType.Resolve());
		}

		public static MethodInfo ResolveMethod(string assemblyName, string declaringTypeName, string methodName, BindingFlags bindingFlags, params string[] paramTypes)
		{
			var containingAssembly = Assembly.Load(new AssemblyName(assemblyName));
			var declaringType = containingAssembly.GetType(declaringTypeName);

			return declaringType.GetMethod(methodName,
			                               bindingFlags, 
										   null,
			                               paramTypes.Select(typeName => Type.GetType(typeName)).ToArray(), 
										   null);
		}
	}
}
