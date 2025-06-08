using System;
using System.Diagnostics.CodeAnalysis;
using Cecilifier.Core.AST;
using Cecilifier.Core.Variables;
using Microsoft.CodeAnalysis;
using static Cecilifier.Core.Misc.Utils;

namespace Cecilifier.Core.Extensions
{
    internal static class FieldExtensions
    {
        public static string FieldResolverExpression(this IFieldSymbol field, IVisitorContext context)
        {
            if (field.IsDefinedInCurrentAssembly(context))
            {
                var found = context.DefinitionVariables.GetVariable(field.Name, VariableMemberKind.Field, field.ContainingType.OriginalDefinition.ToDisplayString());
                ThrowIfVariableNotFound(found.IsValid);

                var resolvedField = field.ContainingType.IsGenericType
                    ? $$"""new FieldReference({{found.VariableName}}.Name, {{found.VariableName}}.FieldType, {{context.TypeResolver.Resolve(field.ContainingType)}})""" 
                    : found.VariableName;
                
                return resolvedField;
            }

            var declaringTypeName = field.ContainingType.FullyQualifiedName();
            return ImportFromMainModule($"TypeHelpers.ResolveField(\"{declaringTypeName}\",\"{field.Name}\")");

            [ExcludeFromCodeCoverage]
            void ThrowIfVariableNotFound(bool found) { if(!found) throw new Exception($"Failed to resolve variable with field definition for `{field}`"); }
        }
    }
}
