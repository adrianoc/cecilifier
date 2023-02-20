using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

public class Program
{
    public static void Main(string[] args)
    {
        var summary = BenchmarkRunner.Run<CecilifierExtensionsBenchmarks>();
    }
}

[MemoryDiagnoser]
public class CecilifierExtensionsBenchmarks
{
    [ParamsSource(nameof(PascalCaseValues))]
    public string PascalCaseValue { get; set; }

    [Benchmark(Baseline = true)]
    public string PascalCaseNaive()
    {
        return PascalCaseValue.Length > 1
             ? char.ToUpper(PascalCaseValue[0]) + PascalCaseValue.Substring(1)
             : PascalCaseValue;
    }

    [Benchmark]
    public string PascalCaseSpan()
    {
        Span<char> copySpan = stackalloc char[PascalCaseValue.Length];
        PascalCaseValue.AsSpan().CopyTo(copySpan);

        if (copySpan.Length > 1)
        {
            copySpan[0] = Char.ToUpper(copySpan[0]);
        }
        return copySpan.ToString();
    }

    public static IEnumerable<string> PascalCaseValues() => new[]
    {
        string.Empty,
        "common case",
        "some relatively large string to process",
        new String('a', 256)
    };
}
