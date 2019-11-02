using System.Collections.Generic;
using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    internal class CustomAttributeComparer : IEqualityComparer<CustomAttributeArgument>
    {
        static CustomAttributeComparer()
        {
            Instance = new CustomAttributeComparer();
        }

        public static IEqualityComparer<CustomAttributeArgument> Instance { get; }

        public bool Equals(CustomAttributeArgument x, CustomAttributeArgument y)
        {
            if (x.Type.ToString() != y.Type.ToString())
            {
                return false;
            }

            if (x.Value != null && y.Value == null)
            {
                return false;
            }

            if (x.Value == null && y.Value != null)
            {
                return false;
            }

            return x.Value != null
                ? x.Value.ToString() == y.Value.ToString()
                : true;
        }

        public int GetHashCode(CustomAttributeArgument obj)
        {
            return 0;
        }
    }
}
