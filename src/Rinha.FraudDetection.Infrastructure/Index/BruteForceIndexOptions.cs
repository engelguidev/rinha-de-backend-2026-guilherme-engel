namespace Rinha.FraudDetection.Infrastructure.Index;

public sealed class BruteForceIndexOptions
{
    public string IndexPath { get; init; } = "data/knn.idx";
    public int MaxPartitionsToScan { get; init; } = 2;
    public int MaxPartitionItems { get; init; } = 0;
    public bool HardPartitionLimit { get; init; } = false;
    public bool PartitionOnly { get; init; } = false;
}
