#nullable enable
using System;

namespace Cecilifier.Core.ApiDriver.DefinitionsFactory;

public readonly struct FieldInitializationData(byte[] initializationData, object? constantValue = null)
{
    public byte[]? InitializationData { get; } = initializationData;
    public object? ConstantValue { get; } = constantValue;
    
    public static implicit operator FieldInitializationData(string value) => new(Array.Empty<byte>(), value);
    public static implicit operator FieldInitializationData(int value) => new(Array.Empty<byte>(), value);
}
