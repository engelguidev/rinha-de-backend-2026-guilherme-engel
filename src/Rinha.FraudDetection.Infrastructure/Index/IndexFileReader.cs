using System.Buffers.Binary;
using System.Linq;

namespace Rinha.FraudDetection.Infrastructure.Index;

public sealed class IndexFileReader
{
    private readonly string _path;

    private short[] _vectors = Array.Empty<short>();
    private byte[] _labels = Array.Empty<byte>();
    private PartitionEntry[] _partitions = new PartitionEntry[IndexFileFormat.PartitionCount];
    private bool _loaded;

    public IndexFileReader(string path)
    {
        _path = path;
    }

    public int Count { get; private set; }

    public PartitionEntry[] Partitions => _partitions;

    public short[] Vectors => _vectors;

    public byte[] Labels => _labels;

    public void Load()
    {
        if (_loaded)
        {
            return;
        }

        using var stream = File.OpenRead(_path);
        using var reader = new BinaryReader(stream);

        var header = reader.ReadBytes(IndexFileFormat.HeaderSize);
        if (header.Length != IndexFileFormat.HeaderSize)
        {
            throw new InvalidDataException("Invalid index header.");
        }

        var magic = new string(header.Take(8).Select(b => (char)b).ToArray());
        if (!string.Equals(magic, IndexFileFormat.Magic, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid index magic.");
        }

        var scale = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
        var dims = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12));
        if (scale != IndexFileFormat.Scale || dims != IndexFileFormat.Dims)
        {
            throw new InvalidDataException("Index format mismatch.");
        }

        Count = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16));
        var partitionCount = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20));
        var entrySize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(24));
        if (partitionCount != IndexFileFormat.PartitionCount || entrySize != IndexFileFormat.PartitionEntrySize)
        {
            throw new InvalidDataException("Partition table mismatch.");
        }

        _partitions = new PartitionEntry[IndexFileFormat.PartitionCount];
        for (var i = 0; i < IndexFileFormat.PartitionCount; i++)
        {
            var entryBytes = reader.ReadBytes(IndexFileFormat.PartitionEntrySize);
            if (entryBytes.Length != IndexFileFormat.PartitionEntrySize)
            {
                throw new InvalidDataException("Partition table truncated.");
            }
            _partitions[i] = PartitionEntry.FromBytes(entryBytes);
        }

        var vectorCount = Count * IndexFileFormat.Dims;
        _vectors = new short[vectorCount];
        for (var i = 0; i < vectorCount; i++)
        {
            _vectors[i] = reader.ReadInt16();
        }

        _labels = reader.ReadBytes(Count);
        if (_labels.Length != Count)
        {
            throw new InvalidDataException("Label section truncated.");
        }

        _loaded = true;
    }

    public readonly record struct PartitionEntry(
        uint Key,
        int Start,
        int Length,
        short[] Min,
        short[] Max)
    {
        public static PartitionEntry FromBytes(byte[] buffer)
        {
            var key = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(0));
            var start = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(4));
            var length = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(8));

            var min = new short[IndexFileFormat.Dims];
            var max = new short[IndexFileFormat.Dims];

            var offset = 12;
            for (var i = 0; i < IndexFileFormat.Dims; i++)
            {
                min[i] = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset));
                offset += 2;
            }
            for (var i = 0; i < IndexFileFormat.Dims; i++)
            {
                max[i] = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset));
                offset += 2;
            }

            return new PartitionEntry(key, start, length, min, max);
        }
    }
}
