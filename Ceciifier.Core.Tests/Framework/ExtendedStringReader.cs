using System.IO;

namespace Ceciifier.Core.Tests.Framework
{
	class ExtendedStringReader : StringReader
	{
		public ExtendedStringReader(string s) : base(s)
		{
		}

		public int IgnoreNextLines { get; set; }

		public override string ReadLine()
		{
			string line;
			while ((line = base.ReadLine()) != null && IgnoreNextLines-- > 0)
				;

			return line;
		}
	}
}
