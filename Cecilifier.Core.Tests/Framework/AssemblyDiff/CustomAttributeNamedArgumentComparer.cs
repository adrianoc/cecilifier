using System.Collections.Generic;
using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    internal class CustomAttributeNamedArgumentComparer : IEqualityComparer<CustomAttributeNamedArgument>
    {
        static CustomAttributeNamedArgumentComparer()
        {
            Instance = new CustomAttributeNamedArgumentComparer();
        }

        public static IEqualityComparer<CustomAttributeNamedArgument> Instance { get; }

        public bool Equals(CustomAttributeNamedArgument x, CustomAttributeNamedArgument y)
        {
            if (x.Name != y.Name)
            {
                return false;
            }

            return CustomAttributeComparer.Instance.Equals(x.Argument, y.Argument);
        }

        public int GetHashCode(CustomAttributeNamedArgument obj)
        {
            return 0;
        }
    }
}
