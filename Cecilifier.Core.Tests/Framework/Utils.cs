using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cecilifier.Core.Tests.Framework
{
    public struct Utils
    {
        public static IReadOnlyList<string> GetTrustedAssembliesPath()
        {
            return ((string) AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).ToList();
        }
    }
}
