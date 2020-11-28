namespace Cecilifier.Core.Misc
{
    internal struct Utils
    {
        public static string ImportFromMainModule(string expression)
        {
            return $"assembly.MainModule.ImportReference({expression})";
        }
    }
}
