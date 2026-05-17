using Rinha.FraudDetection.Application.Interfaces;
using Rinha.FraudDetection.Application.Models;
using Rinha.FraudDetection.Domain.ValueObjects;

namespace Rinha.FraudDetection.Infrastructure.Index;

public sealed class ReferenceDataStore : IVectorIndex, IVectorSearch
{
    private readonly ReferenceDatasetLoader _loader;
    private float[] _vectors = Array.Empty<float>();
    private bool[] _labels = Array.Empty<bool>();
    private bool _initialized;

    public ReferenceDataStore(ReferenceDatasetLoader loader)
    {
        _loader = loader;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        var dataset = await _loader.LoadAsync(cancellationToken);
        _vectors = dataset.Vectors;
        _labels = dataset.Labels;
        _initialized = true;
    }

    public Task<SearchOutcome> SearchAsync(Vector14 vector, int k, CancellationToken cancellationToken)
    {
        if (!_initialized || _labels.Length == 0)
        {
            return Task.FromResult(new SearchOutcome(0, 0));
        }

        var count = _labels.Length;
        var bestDistances = new float[k];
        var bestIndices = new int[k];

        for (var i = 0; i < k; i++)
        {
            bestDistances[i] = float.PositiveInfinity;
            bestIndices[i] = -1;
        }

        var query = vector.Values;
        for (var i = 0; i < count; i++)
        {
            var offset = i * 14;
            var dist = 0f;
            for (var d = 0; d < 14; d++)
            {
                var diff = query[d] - _vectors[offset + d];
                dist += diff * diff;
            }

            if (dist >= bestDistances[k - 1])
            {
                continue;
            }

            var pos = k - 1;
            while (pos > 0 && dist < bestDistances[pos - 1])
            {
                bestDistances[pos] = bestDistances[pos - 1];
                bestIndices[pos] = bestIndices[pos - 1];
                pos--;
            }

            bestDistances[pos] = dist;
            bestIndices[pos] = i;
        }

        var found = 0;
        for (var i = 0; i < k; i++)
        {
            if (bestIndices[i] >= 0)
            {
                found++;
            }
        }

        var neighbors = found == k ? bestIndices : bestIndices.Where(i => i >= 0).ToArray();
        if (neighbors.Length == 0)
        {
            return Task.FromResult(new SearchOutcome(0, 0));
        }

        var fraudCount = 0;
        foreach (var index in neighbors)
        {
            if (index >= 0 && index < _labels.Length && _labels[index])
            {
                fraudCount++;
            }
        }

        return Task.FromResult(new SearchOutcome(fraudCount, neighbors.Length));
    }
}
