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
}
