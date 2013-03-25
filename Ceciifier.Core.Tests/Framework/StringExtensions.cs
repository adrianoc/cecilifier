using System.IO;

namespace Ceciifier.Core.Tests.Framework
{
	static class StringExtensions
	{
		public static string GetPathOfTextResource(this string resourceName, string type, TestKind kind)
		{
			var basePath = Path.Combine("TestResources", kind.ToString());
			return Path.Combine(basePath, resourceName + "." + type + ".txt");
		}

		public static string GetPathOfBinaryResource(this string resourceName, string type)
		{
			return Path.Combine("TestResources", resourceName + type);
		}
	}
}
