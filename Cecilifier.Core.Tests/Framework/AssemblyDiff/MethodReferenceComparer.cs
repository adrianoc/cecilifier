using System.Collections.Generic;
using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    public class MethodReferenceComparer : IComparer<MethodReference>
    {
        public static MethodReferenceComparer Instance = new MethodReferenceComparer();

        public int Compare(MethodReference x, MethodReference y)
        {
            var nameComp = x.DeclaringType.FullName.CompareTo(y.DeclaringType.FullName);

            if (nameComp != 0)
            {
                return nameComp;
            }

            if (x.HasThis ^ y.HasThis)
            {
                return -1;
            }

            if (x.IsGenericInstance ^ y.IsGenericInstance)
            {
                return -1;
            }

            if (x.HasGenericParameters ^ y.HasGenericParameters)
            {
                return -1;
            }

            var genParamComp = x.GenericParameters.Count - y.GenericParameters.Count;
            if (genParamComp != 0)
            {
                return genParamComp;
            }

            if (x.ExplicitThis ^ y.ExplicitThis)
            {
                return -1;
            }

            var returnTypeComp = x.ReturnType.FullName.CompareTo(y.ReturnType.FullName);
            if (returnTypeComp != 0)
            {
                return returnTypeComp;
            }

            if (x.HasParameters ^ y.HasParameters)
            {
                return -1;
            }

            var paramComp = x.Parameters.Count - y.Parameters.Count;
            if (paramComp != 0)
            {
                return paramComp;
            }

            for (var i = 0; i < x.Parameters.Count; i++)
            {
                var paramTypeComp = x.Parameters[i].ParameterType.FullName.CompareTo(y.Parameters[i].ParameterType.FullName);
                if (paramTypeComp != 0)
                {
                    return paramTypeComp;
                }

                if (x.Parameters[i].Attributes != y.Parameters[i].Attributes)
                {
                    return -1;
                }
            }

            return 0;
        }
    }
}
