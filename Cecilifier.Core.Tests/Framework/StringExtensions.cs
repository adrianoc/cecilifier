using System.IO;

namespace Cecilifier.Core.Tests.Framework
{
    internal static class StringExtensions
    {
        public static string GetPathOfTextResource(this string resourceName, string type)
        {
            return GetPathOfResource(resourceName, "." + type + ".txt");
        }

        public static string GetPathOfBinaryResource(this string resourceName, string type)
        {
            return GetPathOfResource(resourceName, type);
        }

        private static string GetPathOfResource(string resourceName, string type)
        {
            return Path.Combine("TestResources/Integration", resourceName + type);
        }
    }
}
