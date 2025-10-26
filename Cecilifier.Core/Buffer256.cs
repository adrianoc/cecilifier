using System.Runtime.CompilerServices;

namespace Cecilifier.Core;

// Simple inline array with 256 elements. After migrating to .NET 10 we can use the newly introduced types in 
// the BCL and remove this one.
[InlineArray(256)]
public struct Buffer256<T>
{
    private T _data;
}
