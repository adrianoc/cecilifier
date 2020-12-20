using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    internal class BaseStrictDiffVisitor
    {
        protected readonly TextWriter output;

        protected BaseStrictDiffVisitor(TextWriter output)
        {
            this.output = output;
        }

        internal bool ValidateGenericParameters(Collection<GenericParameter> source, Collection<GenericParameter> target, string sourceFileName, string targetFileName)
        {
            if (source.Count != target.Count)
            {
                output.WriteLine($"[{sourceFileName}] Mismatch in generic parameters.\n\t{ToString(source)} : {sourceFileName}\n\t{ToString(target)}: {targetFileName}");
                return false;
            }

            var ret = true;
            var i = 0;
            foreach (var sourceParam in source)
            {
                var targetParam = target[i++];
                if (sourceParam.IsContravariant != targetParam.IsContravariant)
                {
                    output.WriteLine($"Difference in contra-variance for '{sourceParam.Name}' :\n\tSource ({sourceFileName}): {sourceParam.IsContravariant}\n\tTarget ({targetFileName}): {targetParam.IsContravariant}");
                    ret = false;
                }
                
                if (sourceParam.IsCovariant != targetParam.IsCovariant)
                {
                    output.WriteLine($"Difference in co-variance for '{sourceParam.Name}' :\n\tSource ({sourceFileName}): {sourceParam.IsCovariant}\n\tTarget ({targetFileName}): {targetParam.IsCovariant}");
                    ret = false;
                }

                if (sourceParam.HasReferenceTypeConstraint != targetParam.HasReferenceTypeConstraint)
                {
                    output.WriteLine($"Difference in 'class' constraint for '{sourceParam.Name}' :\n\tSource ({sourceFileName}): {sourceParam.HasReferenceTypeConstraint}\n\tTarget ({targetFileName}): {targetParam.HasReferenceTypeConstraint}");
                    ret = false;
                }
                
                if (sourceParam.HasDefaultConstructorConstraint != targetParam.HasDefaultConstructorConstraint)
                {
                    output.WriteLine($"Difference in 'new()' constraint for '{sourceParam.Name}' :\n\tSource ({sourceFileName}): {sourceParam.HasDefaultConstructorConstraint}\n\tTarget ({targetFileName}): {targetParam.HasDefaultConstructorConstraint}");
                    ret = false;
                }

                if (sourceParam.HasNotNullableValueTypeConstraint ^ targetParam.HasNotNullableValueTypeConstraint)
                {
                    output.WriteLine($"Difference in 'struct' constraint for '{sourceParam.Name}' :\n\tSource ({sourceFileName}): {sourceParam.HasReferenceTypeConstraint}\n\tTarget ({targetFileName}): {targetParam.HasNotNullableValueTypeConstraint}");
                    ret = false;
                }
                
                
                if (sourceParam.Constraints.Count != targetParam.Constraints.Count)
                {
                    output.WriteLine($"# of constrains differs for type parameter '{sourceParam.Name}' :\n\tSource ({sourceFileName}): {sourceParam.Constraints.Count}\n\t{string.Join(',', sourceParam.Constraints.Select(c => c.ConstraintType))}\n\tTarget ({targetFileName}): {targetParam.Constraints.Count}\n\t{string.Join(',', targetParam.Constraints.Select(c => c.ConstraintType))}");
                    ret = false;
                    continue;
                }

                var sortedTargetConstraintTypes = targetParam.Constraints.OrderBy(c => c.ConstraintType.FullName).ToArray();
                var constraintIndex = 0;
                foreach (var sourceConstraint in sourceParam.Constraints.OrderBy(c => c.ConstraintType.FullName))
                {
                    var targetConstraint = sortedTargetConstraintTypes[constraintIndex++];
                    if (sourceConstraint.ConstraintType.FullName != targetConstraint.ConstraintType.FullName)
                    {
                        output.WriteLine($"Generic constraint types ({sourceConstraint.ConstraintType.FullName} / {targetConstraint.ConstraintType.FullName}) differ on generic type parameter '{sourceParam.Name}'");
                        return false;
                    }
                }
            }
            
            return ret;
        
            string ToString(IEnumerable<GenericParameter> genericParameters)
            {
                if (!genericParameters.Any())
                    return "None";
                
                return string.Join(',', genericParameters.Select(gp => gp.Name));
            }
        }
    }
}
