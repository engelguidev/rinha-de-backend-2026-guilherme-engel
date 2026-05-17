using Rinha.FraudDetection.Application.Models;
using Rinha.FraudDetection.Domain.Models;
using Rinha.FraudDetection.Domain.ValueObjects;

namespace Rinha.FraudDetection.Application.Interfaces;

public interface IVectorizer
{
    Vector14 Vectorize(TransactionPayload payload, NormalizationConfig normalization, MccRiskTable mccRisk);
}
