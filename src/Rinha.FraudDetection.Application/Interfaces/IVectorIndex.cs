namespace Rinha.FraudDetection.Application.Interfaces;

public interface IVectorIndex
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
