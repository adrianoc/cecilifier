#nullable enable
using System;
using System.Linq;

namespace Cecilifier.Core.ApiDriver.Attributes;

public class CustomAttributeArgument
{
    private object? _value;

    public object? Value
    {
        get => _value ?? Values;
        set
        {
            if (value is Array)
            {
                Values = ((object[]) value).Select(v => new CustomAttributeArgument { Value = v }).ToArray();
            }
            else
            {
                _value = value;
            }
        }
    }
    
    public CustomAttributeArgument[]? Values { get; private set; }

    public override int GetHashCode()
    {
        if (_value != null)
            return _value.GetHashCode();
        
        return Values?.Select(v => v.Value?.GetHashCode() ?? 0).Sum() ?? 0;
    }
}
