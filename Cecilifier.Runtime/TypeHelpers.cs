using System;
using System.Diagnostics;
using System.Globalization;
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
										   null, //new BlaBinder(),
			                               paramTypes.Select(typeName => Type.GetType(typeName)).ToArray(), 
										   null);
		}
	}

	class BlaBinder : Binder
	{
		public override MethodBase BindToMethod(BindingFlags bindingAttr, MethodBase[] match, ref object[] args, ParameterModifier[] modifiers, CultureInfo culture, string[] names, out object state)
		{
			Console.WriteLine(":::::::: {0} called.", new StackFrame().GetMethod().Name);
			throw new NotImplementedException();
		}

		public override MethodBase SelectMethod(BindingFlags bindingAttr, MethodBase[] match, Type[] types, ParameterModifier[] modifiers)
		{
			Console.WriteLine(":::::::: {0} called.", new StackFrame().GetMethod().Name);
			if (match == null)
			{
				throw new ArgumentException("value cannot be null.", "match");
			}

			foreach (var method in match)
			{
				Array.ForEach(method.GetParameters(), pi => Console.WriteLine(" *************>{0} -> {1}", pi.Name, pi.ParameterType.Name));
			}
			
			return match[0];
		}
		
		public override FieldInfo BindToField(BindingFlags bindingAttr, FieldInfo[] match, object value, CultureInfo culture)
		{
			Console.WriteLine(":::::::: {0} called.", new StackFrame().GetMethod().Name);
			throw new NotImplementedException();
		}
	
		public override PropertyInfo SelectProperty(BindingFlags bindingAttr, PropertyInfo[] match, Type returnType, Type[] indexes, ParameterModifier[] modifiers)
		{
			Console.WriteLine(":::::::: {0} called.", new StackFrame().GetMethod().Name);
			throw new NotImplementedException();
		}

		public override object ChangeType(object value, Type type, CultureInfo culture)
		{
			Console.WriteLine(":::::::: {0} called.", new StackFrame().GetMethod().Name);
			throw new NotImplementedException();
		}

		public override void ReorderArgumentArray(ref object[] args, object state)
		{
			Console.WriteLine(":::::::: {0} called.", new StackFrame().GetMethod().Name);
			throw new NotImplementedException();
		}
	}
}
