using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cecilifier.Core.AST;
using Cecilifier.Core.Naming;

namespace Cecilifier.Core.Extensions
{
    public static class StringExtensions
    {
        internal static string[] CloneMethodReferenceOverriding(this string methodRef, IVisitorContext context, Dictionary<string, string> overridenProperties, bool hasParameters, out string resolvedVariable)
        {
            var exps = new List<string>();
            var cloned = new StringBuilder($"new MethodReference({methodRef}.Name, {methodRef}.ReturnType) {{ ");
            foreach (var propName in methodReferencePropertiesToClone)
            {
                if (!overridenProperties.TryGetValue(propName, out var propValue))
                {
                    propValue = $"{methodRef}.{propName}";
                }

                cloned.AppendFormat($" {propName} = {propValue},");
            }

            cloned.Append("}");
            
            if (hasParameters)
            {
                resolvedVariable = context.Naming.SyntheticVariable("m", ElementKind.Method);
                
                exps.Add($"var {resolvedVariable} = {cloned};");
                exps.Add($"foreach(var p in {methodRef}.Parameters)");
                exps.Add("{");
                exps.Add($"\t{resolvedVariable}.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));");
                exps.Add("}");
            }
            else
            {
                resolvedVariable = cloned.ToString();
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
