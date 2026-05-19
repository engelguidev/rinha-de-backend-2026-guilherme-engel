using Rinha.FraudDetection.Application.Interfaces;
using Rinha.FraudDetection.Application.Models;
using Rinha.FraudDetection.Domain.Models;

namespace Rinha.FraudDetection.Application.UseCases;

public sealed class DetectFraudUseCase
{
    private readonly IVectorizer _vectorizer;
    private readonly IVectorSearch _search;
    private readonly IResourceProvider _resourceProvider;
    private readonly FraudDetectionOptions _options;

    public DetectFraudUseCase(
        IVectorizer vectorizer,
        IVectorSearch search,
        IResourceProvider resourceProvider,
        FraudDetectionOptions options)
    {
        _vectorizer = vectorizer;
        _search = search;
        _resourceProvider = resourceProvider;
        _options = options;
    }

    public async Task<FraudDecision> ExecuteAsync(TransactionPayload payload, CancellationToken cancellationToken)
    {
        var normalization = await _resourceProvider.GetNormalizationAsync(cancellationToken);
        var mccRisk = await _resourceProvider.GetMccRiskAsync(cancellationToken);

        var vector = _vectorizer.Vectorize(payload, normalization, mccRisk);
        var k = _options.KnnK <= 0 ? 5 : _options.KnnK;
        var outcome = await _search.SearchAsync(vector, k, cancellationToken);
        if (outcome.Total == 0)
        {
            return new FraudDecision(true, 0.0f);
        }

        if (outcome.Total < k)
        {
            Console.Error.WriteLine($"KNN returned {outcome.Total}/{k} neighbors.");
        }

        var score = (float)outcome.FraudCount / outcome.Total;
        return new FraudDecision(score < _options.FraudThreshold, score);
    }
}
