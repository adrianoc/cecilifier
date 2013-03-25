using System.IO;

namespace Cecilifier.Core.Tests.Framework
{
	static class StringExtensions
	{
		public static string GetPathOfTextResource(this string resourceName, string type, TestKind kind)
		{
			return GetPathOfResource(resourceName, "." + type + ".txt", kind);
		}

		public static string GetPathOfBinaryResource(this string resourceName, string type, TestKind kind)
		{
			return GetPathOfResource(resourceName, type, kind);
		}

		private static string GetPathOfResource(string resourceName, string type, TestKind kind)
		{
			var basePath = Path.Combine("TestResources", kind.ToString());
			return Path.Combine(basePath, resourceName + type);
		}
	}
}
