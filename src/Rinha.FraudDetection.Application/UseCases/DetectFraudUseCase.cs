using Rinha.FraudDetection.Application.Interfaces;
using Rinha.FraudDetection.Domain.Models;

namespace Rinha.FraudDetection.Application.UseCases;

public sealed class DetectFraudUseCase
{
    private readonly IVectorizer _vectorizer;
    private readonly IVectorSearch _search;
    private readonly IResourceProvider _resourceProvider;

    public DetectFraudUseCase(
        IVectorizer vectorizer,
        IVectorSearch search,
        IResourceProvider resourceProvider)
    {
        _vectorizer = vectorizer;
        _search = search;
        _resourceProvider = resourceProvider;
    }

    public async Task<FraudDecision> ExecuteAsync(TransactionPayload payload, CancellationToken cancellationToken)
    {
        var normalization = await _resourceProvider.GetNormalizationAsync(cancellationToken);
        var mccRisk = await _resourceProvider.GetMccRiskAsync(cancellationToken);

        var vector = _vectorizer.Vectorize(payload, normalization, mccRisk);
        var outcome = await _search.SearchAsync(vector, 5, cancellationToken);
        if (outcome.Total == 0)
        {
            return new FraudDecision(true, 0.0f);
        }

        var score = (float)outcome.FraudCount / outcome.Total;
        return new FraudDecision(score <= 0.6f, score);
    }
}
