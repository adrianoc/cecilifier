using System.IO;

namespace Ceciifier.Core.Tests.Framework
{
	static class StringExtensions
	{
		public static string GetPathOf(this string resourceName, string type)
		{
			return Path.Combine("TestResources", resourceName + "." + type + ".txt");
		}
	}
}
