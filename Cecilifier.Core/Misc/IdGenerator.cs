using System.Collections.Generic;

namespace Cecilifier.Core.Misc
{
	class IdGenerator
	{
		public int IdFor(string key)
		{
			var nextId = 1;
			if (idMap.ContainsKey(key))
			{
				nextId = idMap[key] + 1;
			}

			idMap[key] = nextId;

			return nextId;
		}

		private IDictionary<string, int> idMap = new Dictionary<string, int>();
	}
}
