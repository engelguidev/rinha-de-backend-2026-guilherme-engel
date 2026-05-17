using Rinha.FraudDetection.Application.Interfaces;
using Rinha.FraudDetection.Domain.ValueObjects;

namespace Rinha.FraudDetection.Infrastructure.Index;

public sealed class HnswVectorIndex : IVectorIndex
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public int[] Search(Vector14 vector, int k)
    {
        return Array.Empty<int>();
    }
}
