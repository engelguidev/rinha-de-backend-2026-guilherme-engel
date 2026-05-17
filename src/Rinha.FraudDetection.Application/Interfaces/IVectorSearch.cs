using Rinha.FraudDetection.Application.Models;
using Rinha.FraudDetection.Domain.ValueObjects;

namespace Rinha.FraudDetection.Application.Interfaces;

public interface IVectorSearch
{
    Task<SearchOutcome> SearchAsync(Vector14 vector, int k, CancellationToken cancellationToken);
}
