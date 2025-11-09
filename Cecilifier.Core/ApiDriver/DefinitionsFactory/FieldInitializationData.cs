#nullable enable
using System;

namespace Cecilifier.Core.ApiDriver.DefinitionsFactory;

public readonly ref struct FieldInitializationData(ReadOnlySpan<byte> initializationData, object? constantValue = null)
{
    public ReadOnlySpan<byte> InitializationData { get; } = initializationData;
    public object? ConstantValue { get; } = constantValue;
    
    public static implicit operator FieldInitializationData(string value) => new(ReadOnlySpan<byte>.Empty, value);
    public static implicit operator FieldInitializationData(int value) => new(ReadOnlySpan<byte>.Empty, value);
}
