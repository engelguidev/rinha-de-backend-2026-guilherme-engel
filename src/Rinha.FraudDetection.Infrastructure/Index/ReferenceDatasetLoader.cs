using System.IO.Compression;
using System.Text.Json;

namespace Rinha.FraudDetection.Infrastructure.Index;

public sealed class ReferenceDatasetLoader
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public ReferenceDatasetLoader(string filePath)
    {
        _filePath = filePath;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<ReferenceDataset> LoadAsync(CancellationToken cancellationToken)
    {
        await using var fileStream = File.OpenRead(_filePath);
        await using var gzip = new GZipStream(fileStream, CompressionMode.Decompress);

        var vectors = new List<float>(1_000_000);
        var labels = new List<bool>(1_000_000);

        await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<ReferenceItem>(gzip, _jsonOptions, cancellationToken))
        {
            if (item?.Vector is null || item.Vector.Length != 14)
            {
                continue;
            }

            vectors.AddRange(item.Vector);
            labels.Add(string.Equals(item.Label, "fraud", StringComparison.OrdinalIgnoreCase));
        }

        return new ReferenceDataset(vectors.ToArray(), labels.ToArray());
    }

    private sealed class ReferenceItem
    {
        public float[]? Vector { get; init; }
        public string? Label { get; init; }
    }
}
