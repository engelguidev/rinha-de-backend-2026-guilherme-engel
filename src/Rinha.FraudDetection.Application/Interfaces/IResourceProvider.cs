using Rinha.FraudDetection.Application.Models;

namespace Rinha.FraudDetection.Application.Interfaces;

public interface IResourceProvider
{
    Task<NormalizationConfig> GetNormalizationAsync(CancellationToken cancellationToken);
    Task<MccRiskTable> GetMccRiskAsync(CancellationToken cancellationToken);
}
