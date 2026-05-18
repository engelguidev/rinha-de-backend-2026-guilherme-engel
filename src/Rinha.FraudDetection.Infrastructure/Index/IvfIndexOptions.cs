namespace Rinha.FraudDetection.Infrastructure.Index;

public sealed class IvfIndexOptions
{
    public string IndexPath { get; init; } = "data/ivf.idx";
    public int ClusterCount { get; init; } = 256;
    public int NProbe { get; init; } = 1;
    public int MaxVectorsPerCluster { get; init; } = 0;
}
