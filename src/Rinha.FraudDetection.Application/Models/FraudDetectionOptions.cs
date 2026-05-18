namespace Rinha.FraudDetection.Application.Models;

public sealed class FraudDetectionOptions
{
    public int KnnK { get; init; } = 5;
    public float FraudThreshold { get; init; } = 0.6f;
}
