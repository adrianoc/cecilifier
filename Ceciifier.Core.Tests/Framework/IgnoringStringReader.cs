using System.IO;

namespace Ceciifier.Core.Tests.Framework
{
	class IgnoringStringReader : StringReader
	{
		public IgnoringStringReader(string s) : base(s)
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
