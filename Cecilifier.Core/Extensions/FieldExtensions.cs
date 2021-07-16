﻿using System;
using Cecilifier.Core.AST;
using Microsoft.CodeAnalysis;
using static Cecilifier.Core.Misc.Utils;

namespace Cecilifier.Core.Extensions
{
    internal static class FieldExtensions
    {
        public static string FieldResolverExpression(this IFieldSymbol field, IVisitorContext context)
        {
            if (field.IsDefinedInCurrentType(context))
            {
                var found = context.DefinitionVariables.GetVariable(field.Name, MemberKind.Field, field.ContainingType.Name);
                if (!found.IsValid)
                {
                    throw new Exception($"Failed to resolve variable with field definition for `{field}`");
                }
                
                return found.VariableName;
            }

            var declaringTypeName = field.ContainingType.FullyQualifiedName();

            return ImportFromMainModule($"TypeHelpers.ResolveField(\"{declaringTypeName}\",\"{field.Name}\")");
        }
    }
}
