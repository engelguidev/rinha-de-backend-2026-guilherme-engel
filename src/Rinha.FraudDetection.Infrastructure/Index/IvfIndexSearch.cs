using System.Buffers.Binary;
using Rinha.FraudDetection.Application.Interfaces;
using Rinha.FraudDetection.Application.Models;
using Rinha.FraudDetection.Domain.ValueObjects;

namespace Rinha.FraudDetection.Infrastructure.Index;

public sealed class IvfIndexSearch : IVectorIndex, IVectorSearch
{
    private const string Magic = "RIVF001";
    private const int HeaderSize = 20;

    private readonly IvfIndexOptions _options;
    private float[] _centroids = Array.Empty<float>();
    private int[] _offsets = Array.Empty<int>();
    private short[] _vectors = Array.Empty<short>();
    private byte[] _labels = Array.Empty<byte>();
    private int _count;
    private int _clusters;

    public IvfIndexSearch(IvfIndexOptions options)
    {
        _options = options;
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(_options.IndexPath);
        using var reader = new BinaryReader(stream);

        var header = reader.ReadBytes(HeaderSize);
        if (header.Length != HeaderSize)
        {
            throw new InvalidDataException("IVF header truncated.");
        }

        var magic = System.Text.Encoding.ASCII.GetString(header, 0, 7);
        if (!string.Equals(magic, Magic, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid IVF magic.");
        }

        var dims = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
        _count = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12));
        _clusters = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16));

        if (dims != IndexFileFormat.Dims)
        {
            throw new InvalidDataException("IVF dims mismatch.");
        }

        var centroidCount = _clusters * IndexFileFormat.Dims;
        _centroids = new float[centroidCount];
        for (var i = 0; i < centroidCount; i++)
        {
            _centroids[i] = reader.ReadSingle();
        }

        _offsets = new int[_clusters + 1];
        for (var i = 0; i < _offsets.Length; i++)
        {
            _offsets[i] = reader.ReadInt32();
        }

        _vectors = new short[_count * IndexFileFormat.Dims];
        for (var i = 0; i < _vectors.Length; i++)
        {
            _vectors[i] = reader.ReadInt16();
        }

        _labels = reader.ReadBytes(_count);
        if (_labels.Length != _count)
        {
            throw new InvalidDataException("IVF labels truncated.");
        }

        return Task.CompletedTask;
    }

    public Task<SearchOutcome> SearchAsync(Vector14 vector, int k, CancellationToken cancellationToken)
    {
        if (_count == 0 || _clusters == 0)
        {
            return Task.FromResult(new SearchOutcome(0, 0));
        }

        Span<float> centroidDist = _clusters <= 256 ? stackalloc float[_clusters] : new float[_clusters];
        var values = vector.Values;
        for (var c = 0; c < _clusters; c++)
        {
            var offset = c * IndexFileFormat.Dims;
            var dist = 0f;
            for (var d = 0; d < IndexFileFormat.Dims; d++)
            {
                var diff = values[d] - _centroids[offset + d];
                dist += diff * diff;
            }
            centroidDist[c] = dist;
        }

        var nProbe = Math.Clamp(_options.NProbe, 1, _clusters);
        Span<int> probe = nProbe <= 32 ? stackalloc int[nProbe] : new int[nProbe];
        Span<float> probeDist = nProbe <= 32 ? stackalloc float[nProbe] : new float[nProbe];
        for (var i = 0; i < nProbe; i++)
        {
            probe[i] = -1;
            probeDist[i] = float.PositiveInfinity;
        }

        for (var c = 0; c < _clusters; c++)
        {
            var dist = centroidDist[c];
            if (dist >= probeDist[nProbe - 1])
            {
                continue;
            }

            var pos = nProbe - 1;
            while (pos > 0 && dist < probeDist[pos - 1])
            {
                probeDist[pos] = probeDist[pos - 1];
                probe[pos] = probe[pos - 1];
                pos--;
            }
            probeDist[pos] = dist;
            probe[pos] = c;
        }

        Span<short> q = stackalloc short[IndexFileFormat.Dims];
        for (var d = 0; d < IndexFileFormat.Dims; d++)
        {
            q[d] = Quantization.QuantizeFloat(values[d]);
        }

        Span<long> bestDists = k <= 32 ? stackalloc long[k] : new long[k];
        Span<byte> bestLabels = k <= 32 ? stackalloc byte[k] : new byte[k];
        for (var i = 0; i < k; i++)
        {
            bestDists[i] = long.MaxValue;
            bestLabels[i] = 0;
        }

        for (var i = 0; i < nProbe; i++)
        {
            var cell = probe[i];
            if (cell < 0)
            {
                continue;
            }

            ScanCell(q, cell, bestDists, bestLabels);
        }

        var fraud = 0;
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
                fraud++;
            }
        }

        return Task.FromResult(new SearchOutcome(fraud, total));
    }

    private void ScanCell(ReadOnlySpan<short> query, int cell, Span<long> bestDists, Span<byte> bestLabels)
    {
        var start = _offsets[cell];
        var end = _offsets[cell + 1];
        if (_options.MaxVectorsPerCluster > 0)
        {
            end = Math.Min(end, start + _options.MaxVectorsPerCluster);
        }

        for (var i = start; i < end; i++)
        {
            var offset = i * IndexFileFormat.Dims;
            var dist = 0L;
            for (var d = 0; d < IndexFileFormat.Dims; d++)
            {
                var diff = (long)query[d] - _vectors[offset + d];
                dist += diff * diff;
            }

            if (dist >= bestDists[bestDists.Length - 1])
            {
                continue;
            }

            var pos = bestDists.Length - 1;
            while (pos > 0 && dist < bestDists[pos - 1])
            {
                bestDists[pos] = bestDists[pos - 1];
                bestLabels[pos] = bestLabels[pos - 1];
                pos--;
            }
            bestDists[pos] = dist;
            bestLabels[pos] = _labels[i];
        }
    }
}
