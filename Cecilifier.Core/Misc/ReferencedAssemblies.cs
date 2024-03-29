using System;
using System.IO;
using System.Linq;

namespace Cecilifier.Core.Misc;

public static class ReferencedAssemblies
{
    public static string[] GetTrustedAssembliesPath()
    {
        return ((string) AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).ToArray();
    }
}
