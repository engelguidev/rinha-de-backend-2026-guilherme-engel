namespace Rinha.FraudDetection.Infrastructure.Index;

public sealed class LshIndexOptions
{
    public int Planes { get; init; } = 16;
    public int MaxBucketProbes { get; init; } = 8;
    public int MaxCandidates { get; init; } = 4000;
}
