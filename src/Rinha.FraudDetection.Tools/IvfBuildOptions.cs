namespace Rinha.FraudDetection.Tools;

public sealed class IvfBuildOptions
{
    public string ReferencesFile { get; init; } = "resources/references.json.gz";
    public string OutputPath { get; init; } = "data/ivf.idx";
    public int ClusterCount { get; init; } = 256;
    public int Iterations { get; init; } = 5;
    public int Seed { get; init; } = 42;

    public static IvfBuildOptions FromEnvironment()
    {
        return new IvfBuildOptions
        {
            ReferencesFile = ReadString("REFERENCES_FILE", "resources/references.json.gz"),
            OutputPath = ReadString("IVF_INDEX_PATH", "data/ivf.idx"),
            ClusterCount = ReadInt("IVF_CLUSTERS", 256),
            Iterations = ReadInt("IVF_ITERS", 5),
            Seed = ReadInt("IVF_SEED", 42)
        };
    }

    private static string ReadString(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int ReadInt(string name, int fallback)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
    }
}
