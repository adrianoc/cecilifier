using System;
using System.Collections.Generic;

namespace Cecilifier.Core.Services;

public class GenericInstanceMethodCacheService<TKey, TValue> : IService
{
    public TValue GetOrCreate<TState>(TKey key, TState state, Func<TKey, TState, TValue> factory)
    {
        if (!_entries.TryGetValue(key, out TValue value))
        {
            value = factory(key, state);
            _entries[key] = value;
        }

        return value;
    }

    private Dictionary<TKey, TValue> _entries = new();
}
