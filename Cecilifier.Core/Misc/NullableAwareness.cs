namespace Cecilifier.Core.Misc;

internal enum NullableAwareness
{
    // https://github.com/dotnet/roslyn/blob/main/docs/features/nullable-metadata.md
    NullableOblivious = 0,
    NonNullable = 1,
    Nullable = 2
}
