namespace Rinha.FraudDetection.Infrastructure.Index;

public sealed record ReferenceDataset(float[] Vectors, bool[] Labels)
{
    public int Count => Labels.Length;
}
