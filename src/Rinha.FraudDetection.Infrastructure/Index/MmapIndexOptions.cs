namespace Rinha.FraudDetection.Infrastructure.Index;

public sealed class MmapIndexOptions
{
    public string IndexPath { get; init; } = "data/knn.idx";
    public int MaxPartitionsToScan { get; init; } = 2;
}
