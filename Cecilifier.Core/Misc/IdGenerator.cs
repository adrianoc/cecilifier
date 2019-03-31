using System.Collections.Generic;

namespace Cecilifier.Core.Misc
{
    internal class IdGenerator
    {
        private readonly IDictionary<string, int> idMap = new Dictionary<string, int>();

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
    }
}
