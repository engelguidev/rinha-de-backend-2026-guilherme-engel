using Rinha.FraudDetection.Application.Interfaces;
using Rinha.FraudDetection.Application.Models;
using Rinha.FraudDetection.Domain.ValueObjects;

namespace Rinha.FraudDetection.Infrastructure.Index;

public sealed class MmapIndexSearch : IVectorIndex, IVectorSearch
{
    private readonly MmapIndexOptions _options;
    private IndexFileReader? _reader;

    public MmapIndexSearch(MmapIndexOptions options)
    {
        _options = options;
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        _reader = new IndexFileReader(_options.IndexPath);
        _reader.Load();
        return Task.CompletedTask;
    }

    public Task<SearchOutcome> SearchAsync(Vector14 vector, int k, CancellationToken cancellationToken)
    {
        if (_reader is null || _reader.Count == 0)
        {
            return Task.FromResult(new SearchOutcome(0, 0));
        }

        Span<short> q = stackalloc short[IndexFileFormat.Dims];
        var values = vector.Values;
        for (var i = 0; i < IndexFileFormat.Dims; i++)
        {
            q[i] = Quantization.QuantizeFloat(values[i]);
        }

        var partitions = _reader.Partitions;
        var primaryKey = (int)Quantization.PartitionKey(q);

        Span<long> bestDists = k <= 32 ? stackalloc long[k] : new long[k];
        Span<byte> bestLabels = k <= 32 ? stackalloc byte[k] : new byte[k];
        for (var i = 0; i < k; i++)
        {
            bestDists[i] = long.MaxValue;
            bestLabels[i] = 0;
        }

        if (primaryKey >= 0 && primaryKey < partitions.Length)
        {
            ScanPartition(q, partitions[primaryKey], bestDists, bestLabels);
        }

        var maxPartitions = _options.MaxPartitionsToScan <= 0
            ? partitions.Length
            : Math.Min(_options.MaxPartitionsToScan, partitions.Length);

        Span<int> candidateIndexes = maxPartitions <= 32 ? stackalloc int[maxPartitions] : new int[maxPartitions];
        Span<long> candidateBounds = maxPartitions <= 32 ? stackalloc long[maxPartitions] : new long[maxPartitions];
        var candidateCount = 0;

        for (var i = 0; i < partitions.Length; i++)
        {
            if (i == primaryKey)
            {
                continue;
            }

            var bound = Quantization.LowerBound(q, partitions[i].Min, partitions[i].Max);
            if (bound >= bestDists[k - 1])
            {
                continue;
            }

            InsertCandidate(i, bound, candidateIndexes, candidateBounds, ref candidateCount);
        }

        for (var i = 0; i < candidateCount; i++)
        {
            if (candidateBounds[i] >= bestDists[k - 1])
            {
                break;
            }

            ScanPartition(q, partitions[candidateIndexes[i]], bestDists, bestLabels);
        }

        var fraudCount = 0;
        var total = 0;
        for (var i = 0; i < k; i++)
        {
            if (bestDists[i] == long.MaxValue)
            {
                continue;
            }

            total++;
            if (bestLabels[i] > 0)
            {
                fraudCount++;
            }
        }

        return Task.FromResult(new SearchOutcome(fraudCount, total));
    }

    private void ScanPartition(ReadOnlySpan<short> query, IndexFileReader.PartitionEntry partition,
        Span<long> bestDists, Span<byte> bestLabels)
    {
        if (_reader is null || partition.Length <= 0)
        {
            return;
        }

        var vectors = _reader.Vectors;
        var labels = _reader.Labels;
        var start = partition.Start;
        var end = start + partition.Length;

        for (var i = start; i < end; i++)
        {
            var offset = i * IndexFileFormat.Dims;
            var dist = 0L;
            for (var d = 0; d < IndexFileFormat.Dims; d++)
            {
                var diff = (long)query[d] - vectors[offset + d];
                dist += diff * diff;
            }

            InsertBest(dist, labels[i], bestDists, bestLabels);
        }
    }

    private static void InsertBest(long dist, byte label, Span<long> bestDists, Span<byte> bestLabels)
    {
        var k = bestDists.Length;
        if (dist >= bestDists[k - 1])
        {
            return;
        }

        var pos = k - 1;
        while (pos > 0 && dist < bestDists[pos - 1])
        {
            bestDists[pos] = bestDists[pos - 1];
            bestLabels[pos] = bestLabels[pos - 1];
            pos--;
        }
        bestDists[pos] = dist;
        bestLabels[pos] = label;
    }

    private static void InsertCandidate(int index, long bound,
        Span<int> indexes, Span<long> bounds, ref int count)
    {
        var capacity = indexes.Length;
        if (capacity == 0)
        {
            return;
        }

        if (count < capacity)
        {
            var pos = count;
            while (pos > 0 && bound < bounds[pos - 1])
            {
                bounds[pos] = bounds[pos - 1];
                indexes[pos] = indexes[pos - 1];
                pos--;
            }
            bounds[pos] = bound;
            indexes[pos] = index;
            count++;
            return;
        }

        if (bound >= bounds[count - 1])
        {
            return;
        }

        var insertPos = count - 1;
        while (insertPos > 0 && bound < bounds[insertPos - 1])
        {
            bounds[insertPos] = bounds[insertPos - 1];
            indexes[insertPos] = indexes[insertPos - 1];
            insertPos--;
        }
        bounds[insertPos] = bound;
        indexes[insertPos] = index;
    }
}
