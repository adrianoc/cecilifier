using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cecilifier.Core.AST;
using Cecilifier.Core.Naming;
using Microsoft.CodeAnalysis;

namespace Cecilifier.Core.Extensions
{
    public static class StringExtensions
    {
        internal static string[] CloneMethodReferenceOverriding(this string methodRef, IVisitorContext context, Dictionary<string, string> overridenProperties, IMethodSymbol method, out string resolvedVariable)
        {
            var cloned = new StringBuilder($"new MethodReference({methodRef}.Name, {methodRef}.ReturnType) {{ ");
            foreach (var propName in methodReferencePropertiesToClone)
            {
                if (!overridenProperties.TryGetValue(propName, out var propValue))
                {
                    propValue = $"{methodRef}.{propName}";
                }

                cloned.Append($" {propName} = {propValue},");
            }

            cloned.Append("}");

            if (method.Parameters.Length == 0 && !method.IsGenericMethod)
            {
                resolvedVariable = cloned.ToString();
                return Array.Empty<string>();
            }

            var exps = new List<string>();
            resolvedVariable = context.Naming.SyntheticVariable(method.SafeIdentifier(), ElementKind.MemberReference);

            exps.Add($"var {resolvedVariable} = {cloned};");
            if (method.Parameters.Length > 0)
            {
                exps.Add($"foreach(var p in {methodRef}.Parameters)");
                exps.Add("{");
                exps.Add($"\t{resolvedVariable}.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));");
                exps.Add("}");
            }

            if (method.IsGenericMethod)
            {
                exps.Add($"foreach(var gp in {methodRef}.GenericParameters)");
                exps.Add("{");
                exps.Add($"\t{resolvedVariable}.GenericParameters.Add(new Mono.Cecil.GenericParameter(gp.Name, {resolvedVariable}));");
                exps.Add("}");
            }

            return exps.ToArray();
        }

        public static int CountNewLines(this string value) => value.Count(ch => ch == '\n');

        private static List<string> methodReferencePropertiesToClone = new()
        {
            "HasThis",
            "ExplicitThis",
            "DeclaringType",
            "CallingConvention",
        };
    }
}
