using System.Linq;
using Rinha.FraudDetection.Application.Interfaces;
using Rinha.FraudDetection.Application.Models;
using Rinha.FraudDetection.Domain.ValueObjects;

namespace Rinha.FraudDetection.Infrastructure.Index;

public sealed class LshAnnIndex : IVectorIndex, IVectorSearch
{
    private readonly ReferenceDatasetLoader _loader;
    private readonly LshIndexOptions _options;
    private readonly Random _random;
    private float[] _vectors = Array.Empty<float>();
    private bool[] _labels = Array.Empty<bool>();
    private float[][] _planes = Array.Empty<float[]>();
    private Dictionary<ulong, List<int>> _buckets = new();
    private bool _initialized;

    public LshAnnIndex(ReferenceDatasetLoader loader, LshIndexOptions options)
    {
        _loader = loader;
        _options = options;
        _random = new Random(42);
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
        _planes = BuildRandomPlanes(_options.Planes);
        _buckets = BuildBuckets(_vectors, _options.Planes);
        _initialized = true;
    }

    public Task<SearchOutcome> SearchAsync(Vector14 vector, int k, CancellationToken cancellationToken)
    {
        if (!_initialized || _labels.Length == 0)
        {
            return Task.FromResult(new SearchOutcome(0, 0));
        }

        var signature = ComputeSignature(vector.Values);
        var candidates = new HashSet<int>();

        AddBucketCandidates(signature, candidates);

        var probes = 0;
        var bit = 0;
        while (candidates.Count < k && probes < _options.MaxBucketProbes && bit < _options.Planes)
        {
            var neighborSignature = signature ^ (1UL << bit);
            AddBucketCandidates(neighborSignature, candidates);
            probes++;
            bit++;
        }

        if (candidates.Count == 0)
        {
            return Task.FromResult(new SearchOutcome(0, 0));
        }

        var candidateList = candidates.Count > _options.MaxCandidates
            ? candidates.Take(_options.MaxCandidates).ToArray()
            : candidates.ToArray();

        var neighbors = ComputeTopK(vector.Values, candidateList, k);
        if (neighbors.Length == 0)
        {
            return Task.FromResult(new SearchOutcome(0, 0));
        }

        var fraudCount = 0;
        foreach (var idx in neighbors)
        {
            if (idx >= 0 && idx < _labels.Length && _labels[idx])
            {
                fraudCount++;
            }
        }

        return Task.FromResult(new SearchOutcome(fraudCount, neighbors.Length));
    }

    private void AddBucketCandidates(ulong signature, HashSet<int> candidates)
    {
        if (_buckets.TryGetValue(signature, out var bucket))
        {
            foreach (var idx in bucket)
            {
                candidates.Add(idx);
            }
        }
    }

    private Dictionary<ulong, List<int>> BuildBuckets(float[] vectors, int planes)
    {
        var buckets = new Dictionary<ulong, List<int>>();
        var count = _labels.Length;

        for (var i = 0; i < count; i++)
        {
            var offset = i * 14;
            Span<float> v = stackalloc float[14];
            for (var d = 0; d < 14; d++)
            {
                v[d] = vectors[offset + d];
            }

            var sig = ComputeSignature(v);
            if (!buckets.TryGetValue(sig, out var list))
            {
                list = new List<int>(64);
                buckets[sig] = list;
            }

            list.Add(i);
        }

        return buckets;
    }

    private ulong ComputeSignature(ReadOnlySpan<float> vector)
    {
        ulong signature = 0;
        for (var i = 0; i < _planes.Length; i++)
        {
            var dot = 0f;
            var plane = _planes[i];
            for (var d = 0; d < 14; d++)
            {
                dot += vector[d] * plane[d];
            }

            if (dot >= 0)
            {
                signature |= 1UL << i;
            }
        }

        return signature;
    }

    private float[][] BuildRandomPlanes(int count)
    {
        var planes = new float[count][];
        for (var i = 0; i < count; i++)
        {
            var plane = new float[14];
            for (var d = 0; d < 14; d++)
            {
                plane[d] = (float)(_random.NextDouble() * 2 - 1);
            }

            planes[i] = plane;
        }

        return planes;
    }

    private int[] ComputeTopK(ReadOnlySpan<float> query, int[] candidates, int k)
    {
        var bestDistances = new float[k];
        var bestIndices = new int[k];

        for (var i = 0; i < k; i++)
        {
            bestDistances[i] = float.PositiveInfinity;
            bestIndices[i] = -1;
        }

        foreach (var idx in candidates)
        {
            var offset = idx * 14;
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
            bestIndices[pos] = idx;
        }

        var found = 0;
        for (var i = 0; i < k; i++)
        {
            if (bestIndices[i] >= 0)
            {
                found++;
            }
        }

        if (found == k)
        {
            return bestIndices;
        }

        var result = new int[found];
        var outIdx = 0;
        for (var i = 0; i < k; i++)
        {
            if (bestIndices[i] >= 0)
            {
                result[outIdx++] = bestIndices[i];
            }
        }

        return result;
    }
}
