using System.IO;

namespace Ceciifier.Core.Tests.Framework
{
	static class StringExtensions
	{
		public static string GetPathOfTextResource(this string resourceName, string type)
		{
			return Path.Combine("TestResources", resourceName + "." + type + ".txt");
		}

		public static string GetPathOfBinaryResource(this string resourceName, string type)
		{
			return Path.Combine("TestResources", resourceName + type);
		}
	}
}
