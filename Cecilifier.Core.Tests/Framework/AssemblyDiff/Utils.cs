using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    public class Utils
    {
        internal static bool CheckCustomAttributes(TextWriter output, IMemberDefinition source, IMemberDefinition target)
        {
            if (source.HasCustomAttributes != target.HasCustomAttributes)
            {
                // if all attrs in the expected assembly are in the ignore list we handle as if it had no attrs at all.
                if (source.HasCustomAttributes && source.CustomAttributes.All(ca => compilerEmmitedAttributesToIgnore.Contains(ca.Constructor.DeclaringType.FullName)))
                    return true;

                var sourceFileName = (source is TypeDefinition sourceDef ? sourceDef : source.DeclaringType).Module.FileName;
                var targetFileName = (target is TypeDefinition targetDef ? targetDef : source.DeclaringType).Module.FileName;
                output.WriteLine($"'{source.FullName}'{(!source.HasCustomAttributes ? "don't" : "")} have custom attributes in {sourceFileName} while '{targetFileName}' {(target.HasCustomAttributes ? "does" : "doesn't")} have.");
                return false;
            }

            if (!source.HasCustomAttributes)
            {
                return true;
            }

            foreach (var customAttribute in source.CustomAttributes)
            {
                var found = target.CustomAttributes.Any(candidate => CustomAttributeMatches(candidate, customAttribute));
                if (!found && !compilerEmmitedAttributesToIgnore.Contains(customAttribute.Constructor.DeclaringType.FullName))
                {
                    output.WriteLine($"Custom attribute {customAttribute.Constructor.FullName} not found in {target.FullName}");
                    return false;
                }
            }

            return true;
        }

        private static bool CustomAttributeMatches(CustomAttribute lhs, CustomAttribute rhs)
        {
            if (lhs.Constructor.ToString() != rhs.Constructor.ToString())
            {
                return false;
            }

            if (lhs.HasConstructorArguments != rhs.HasConstructorArguments)
            {
                return false;
            }

            if (lhs.HasConstructorArguments)
            {
                if (!lhs.ConstructorArguments.SequenceEqual(rhs.ConstructorArguments, CustomAttributeComparer.Instance))
                {
                    return false;
                }
            }

            if (lhs.HasProperties != rhs.HasProperties)
            {
                return false;
            }

            if (lhs.HasProperties)
            {
                if (!lhs.Properties.SequenceEqual(rhs.Properties, CustomAttributeNamedArgumentComparer.Instance))
                {
                    return false;
                }
            }

            if (lhs.HasFields != rhs.HasFields)
            {
                return false;
            }

            if (lhs.HasFields)
            {
                if (!lhs.Fields.SequenceEqual(rhs.Fields, CustomAttributeNamedArgumentComparer.Instance))
                {
                    return false;
                }
            }

            return true;
        }
        
        // These attributes are ignored when checking whether 2 types/member attr list matches.
        internal static HashSet<string> compilerEmmitedAttributesToIgnore = new HashSet<string> 
        {
            "Microsoft.CodeAnalysis.EmbeddedAttribute",
            "System.Runtime.CompilerServices.NullableAttribute",
            "System.Runtime.CompilerServices.IsUnmanagedAttribute",
            "System.Runtime.CompilerServices.NullableContextAttribute",
            "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
            "System.Runtime.CompilerServices.IsReadOnlyAttribute"
        };
    }
}
