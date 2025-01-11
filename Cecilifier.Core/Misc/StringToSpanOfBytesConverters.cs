using System;
using System.Buffers;
using System.Numerics;

namespace Cecilifier.Core.Misc;

public struct StringToSpanOfBytesConverters
{
    public static SpanAction<byte, string> For(string fullyQualifiedTypeName) => fullyQualifiedTypeName switch
    {
        "System.Byte" => Byte,
        "System.Int16" => Int16,
        "System.Int32" => Int32,
        "System.Int64" => Int64,
        "System.Char" => Char,
        "System.Boolean" => Boolean,
        _ => throw new ArgumentOutOfRangeException(nameof(fullyQualifiedTypeName), fullyQualifiedTypeName, "No converter registered for the specified type.")
    };
    
    public static SpanAction<byte, string> Int32 => ItemStringToSpanOfBytes<int>;
    public static SpanAction<byte, string> Int64 => ItemStringToSpanOfBytes<long>;
    public static SpanAction<byte, string> Byte => ItemStringToSpanOfBytes<byte>;
    public static SpanAction<byte, string> Int16 => ItemStringToSpanOfBytes<short>;
    public static SpanAction<byte, string> Char => ItemStringToSpanOfBytes<char>;
    public static SpanAction<byte, string> Boolean => BooleanStringToSpanOfBytes;
        
    static void ItemStringToSpanOfBytes<T>(Span<byte> targetSpan, string textValue) where T : IBinaryInteger<T>
    {
        T value = T.Parse(textValue.AsSpan(), null);
        value.WriteLittleEndian(targetSpan);
    }
        
    static void BooleanStringToSpanOfBytes(Span<byte> targetSpan, string textValue)
    {
        bool value = bool.Parse(textValue);
        targetSpan[0] = value ? (byte)1 : (byte)0;
    }
}
