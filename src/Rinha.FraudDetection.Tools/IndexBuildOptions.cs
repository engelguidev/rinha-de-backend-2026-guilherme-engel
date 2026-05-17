namespace Rinha.FraudDetection.Tools;

public sealed class IndexBuildOptions
{
    public string ReferencesFile { get; init; } = "resources/references.json.gz";
    public string OutputPath { get; init; } = "data/knn.idx";

    public static IndexBuildOptions FromEnvironment()
    {
        return new IndexBuildOptions
        {
            ReferencesFile = ReadString("REFERENCES_FILE", "resources/references.json.gz"),
            OutputPath = ReadString("INDEX_PATH", "data/knn.idx")
        };
    }

    private static string ReadString(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
