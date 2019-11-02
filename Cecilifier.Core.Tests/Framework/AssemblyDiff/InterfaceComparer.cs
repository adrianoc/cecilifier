using System.Collections.Generic;
using Mono.Cecil;

namespace Cecilifier.Core.Tests.Framework.AssemblyDiff
{
    internal class InterfaceComparer : IEqualityComparer<InterfaceImplementation>
    {
        public bool Equals(InterfaceImplementation x, InterfaceImplementation y)
        {
            return x.InterfaceType.FullName == y.InterfaceType.FullName;
        }

        public int GetHashCode(InterfaceImplementation obj)
        {
            return obj.InterfaceType.Name.GetHashCode();
        }
    }
}
