using System.Buffers.Binary;
using System.Linq;
using Rinha.FraudDetection.Infrastructure.Index;

namespace Rinha.FraudDetection.Tools;

public sealed class IndexBuilder
{
    public async Task BuildAsync(string referencesFile, string outputPath, CancellationToken cancellationToken)
    {
        var loader = new ReferenceDatasetLoader(referencesFile);
        var dataset = await loader.LoadAsync(cancellationToken);
        if (dataset.Count == 0)
        {
            throw new InvalidOperationException("No vectors loaded.");
        }

        var count = dataset.Count;
        var quantized = new short[count * IndexFileFormat.Dims];
        var labels = new byte[count];
        var partitionLists = new List<int>[IndexFileFormat.PartitionCount];
        var min = new short[IndexFileFormat.PartitionCount][];
        var max = new short[IndexFileFormat.PartitionCount][];

        for (var i = 0; i < IndexFileFormat.PartitionCount; i++)
        {
            partitionLists[i] = new List<int>();
            min[i] = Enumerable.Repeat(short.MaxValue, IndexFileFormat.Dims).ToArray();
            max[i] = Enumerable.Repeat(short.MinValue, IndexFileFormat.Dims).ToArray();
        }

        var q = new short[IndexFileFormat.Dims];
        for (var i = 0; i < count; i++)
        {
            var offset = i * IndexFileFormat.Dims;
            for (var d = 0; d < IndexFileFormat.Dims; d++)
            {
                var value = dataset.Vectors[offset + d];
                var quant = Quantization.QuantizeFloat(value);
                quantized[offset + d] = quant;
                q[d] = quant;
            }

            labels[i] = dataset.Labels[i] ? (byte)1 : (byte)0;

            var key = (int)Quantization.PartitionKey(q);
            partitionLists[key].Add(i);

            for (var d = 0; d < IndexFileFormat.Dims; d++)
            {
                if (q[d] < min[key][d]) min[key][d] = q[d];
                if (q[d] > max[key][d]) max[key][d] = q[d];
            }
        }

        var orderedVectors = new short[count * IndexFileFormat.Dims];
        var orderedLabels = new byte[count];
        var partitions = new IndexFileReader.PartitionEntry[IndexFileFormat.PartitionCount];

        var cursor = 0;
        for (var key = 0; key < IndexFileFormat.PartitionCount; key++)
        {
            var list = partitionLists[key];
            var start = cursor;
            foreach (var idx in list)
            {
                Array.Copy(quantized, idx * IndexFileFormat.Dims, orderedVectors, cursor * IndexFileFormat.Dims, IndexFileFormat.Dims);
                orderedLabels[cursor] = labels[idx];
                cursor++;
            }

            if (list.Count == 0)
            {
                min[key] = new short[IndexFileFormat.Dims];
                max[key] = new short[IndexFileFormat.Dims];
            }

            partitions[key] = new IndexFileReader.PartitionEntry((uint)key, start, list.Count, min[key], max[key]);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);

        WriteHeader(writer, count);
        WritePartitions(writer, partitions);
        WriteVectors(writer, orderedVectors);
        writer.Write(orderedLabels);
    }

    private static void WriteHeader(BinaryWriter writer, int count)
    {
        var header = new byte[IndexFileFormat.HeaderSize];
        Array.Copy(IndexFileFormat.MagicBytes, header, IndexFileFormat.MagicBytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), IndexFileFormat.Scale);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), IndexFileFormat.Dims);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), count);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20), IndexFileFormat.PartitionCount);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), IndexFileFormat.PartitionEntrySize);
        writer.Write(header);
    }

    private static void WritePartitions(BinaryWriter writer, IndexFileReader.PartitionEntry[] partitions)
    {
        var buffer = new byte[IndexFileFormat.PartitionEntrySize];
        foreach (var partition in partitions)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), partition.Key);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), partition.Start);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(8), partition.Length);
            var offset = 12;
            for (var i = 0; i < IndexFileFormat.Dims; i++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset), partition.Min[i]);
                offset += 2;
            }
            for (var i = 0; i < IndexFileFormat.Dims; i++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset), partition.Max[i]);
                offset += 2;
            }
            writer.Write(buffer);
        }
    }

    private static void WriteVectors(BinaryWriter writer, short[] vectors)
    {
        foreach (var value in vectors)
        {
            writer.Write(value);
        }
    }
}
