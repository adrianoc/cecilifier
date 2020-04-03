using System;

namespace Cecilifier.Core.Misc
{
    internal struct Utils
    {
        public static string ImportFromMainModule(string expression)
        {
            return $"TypeHelpers.Fix(assembly.MainModule.ImportReference({expression}), assembly.MainModule)";
        }
    }
}
