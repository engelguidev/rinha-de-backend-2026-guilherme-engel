namespace Rinha.FraudDetection.Tools;

public sealed class BenchmarkOptions
{
    public string ReferencesFile { get; init; } = "resources/references.json.gz";
    public int QueryCount { get; init; } = 200;
    public int Seed { get; init; } = 42;
}
