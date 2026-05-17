using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Rinha.FraudDetection.Application.Interfaces;
using Rinha.FraudDetection.Application.Models;
using Rinha.FraudDetection.Domain.ValueObjects;

namespace Rinha.FraudDetection.Infrastructure.Index;

public sealed class BruteForceIndexSearch : IVectorIndex, IVectorSearch
{
    private readonly string _path;
    private IndexFileReader? _reader;
    private short[] _vectors = Array.Empty<short>();
    private byte[] _labels = Array.Empty<byte>();
    private IndexFileReader.PartitionEntry[] _partitions = Array.Empty<IndexFileReader.PartitionEntry>();
    private int _count;
    private bool _initialized;
    private const int Stride = IndexFileFormat.Dims + 2;

    public BruteForceIndexSearch(string path)
    {
        _path = path;
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        _reader = new IndexFileReader(_path);
        _reader.Load();

        _count = _reader.Count;
        _vectors = GC.AllocateUninitializedArray<short>(_count * Stride, pinned: true);
        Array.Copy(_reader.Vectors, _vectors, _reader.Vectors.Length);
        _labels = GC.AllocateUninitializedArray<byte>(_count, pinned: true);
        Array.Copy(_reader.Labels, _labels, _count);
        _partitions = _reader.Partitions;
        _initialized = true;
        return Task.CompletedTask;
    }

    public Task<SearchOutcome> SearchAsync(Vector14 vector, int k, CancellationToken cancellationToken)
    {
        if (!_initialized || _count == 0)
        {
            return Task.FromResult(new SearchOutcome(0, 0));
        }

        Span<short> q = stackalloc short[Stride];
        var vals = vector.Values;
        for (int i = 0; i < IndexFileFormat.Dims; i++)
        {
            q[i] = Quantization.QuantizeFloat((float)vals[i]);
        }

        q[14] = 0;
        q[15] = 0;

        Span<int> topD = k <= 32 ? stackalloc int[k] : new int[k];
        Span<byte> topL = k <= 32 ? stackalloc byte[k] : new byte[k];
        for (int i = 0; i < k; i++)
        {
            topD[i] = int.MaxValue;
        }

        var qVec = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(q));
        ref var baseRef = ref MemoryMarshal.GetArrayDataReference(_vectors);
        var partitions = _partitions;
        var primaryKey = (int)Quantization.PartitionKey(q);

        if (primaryKey >= 0 && primaryKey < partitions.Length)
        {
            ScanPartition(qVec, partitions[primaryKey], ref baseRef, topD, topL);
        }

        Span<int> candidateIndexes = partitions.Length <= 32 ? stackalloc int[partitions.Length] : new int[partitions.Length];
        Span<int> candidateBounds = partitions.Length <= 32 ? stackalloc int[partitions.Length] : new int[partitions.Length];
        var candidateCount = 0;

        for (int i = 0; i < partitions.Length; i++)
        {
            if (i == primaryKey)
            {
                continue;
            }

            var bound = (int)Quantization.LowerBound(q, partitions[i].Min, partitions[i].Max);
            if (bound >= topD[k - 1])
            {
                continue;
            }

            InsertCandidate(i, bound, candidateIndexes, candidateBounds, ref candidateCount);
        }

        for (var i = 0; i < candidateCount; i++)
        {
            if (candidateBounds[i] >= topD[k - 1])
            {
                break;
            }

            ScanPartition(qVec, partitions[candidateIndexes[i]], ref baseRef, topD, topL);
        }

        var fraudCount = 0;
        for (int i = 0; i < k; i++)
        {
            fraudCount += topL[i];
        }

        return Task.FromResult(new SearchOutcome(fraudCount, k));
    }

    private void ScanPartition(Vector256<short> query, IndexFileReader.PartitionEntry partition, ref short baseRef,
        Span<int> topD, Span<byte> topL)
    {
        if (partition.Length <= 0)
        {
            return;
        }

        var start = partition.Start;
        var end = start + partition.Length;

        for (var idx = start; idx < end; idx++)
        {
            var rVec = Vector256.LoadUnsafe(ref baseRef, (nuint)(idx * Stride));
            var diff = query - rVec;
            var (lo, hi) = Vector256.Widen(diff);
            var sq = lo * lo + hi * hi;
            var dist = Vector256.Sum(sq);

            if (dist >= topD[topD.Length - 1])
            {
                continue;
            }

            var pos = topD.Length - 2;
            while (pos >= 0 && topD[pos] > dist)
            {
                topD[pos + 1] = topD[pos];
                topL[pos + 1] = topL[pos];
                pos--;
            }

            topD[pos + 1] = dist;
            topL[pos + 1] = _labels[idx];
        }
    }

    private static void InsertCandidate(int index, int bound, Span<int> indexes, Span<int> bounds, ref int count)
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
