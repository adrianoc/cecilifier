using System;
using System.Collections.Generic;

namespace Cecilifier.Core.Services;

public class ServiceCollection
{
    private Dictionary<Type, IService> _items = new();
    public void Add<T>(T t) where T : IService
    {
        _items[typeof(T)] = t;
    }

    public T Get<T>() where T : IService
    {
        return (T) _items[typeof(T)];
    }
}
