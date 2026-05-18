using System.Buffers.Binary;
using Rinha.FraudDetection.Infrastructure.Index;

namespace Rinha.FraudDetection.Tools;

public sealed class IvfIndexBuilder
{
    private const string Magic = "RIVF001";
    private const int ProgressEvery = 5000;

    public async Task BuildAsync(IvfBuildOptions options, CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        Console.WriteLine($"[ivf] loading dataset: {options.ReferencesFile}");
        var loader = new ReferenceDatasetLoader(options.ReferencesFile);
        var dataset = await loader.LoadAsync(cancellationToken);
        if (dataset.Count == 0)
        {
            throw new InvalidOperationException("No vectors loaded.");
        }

        Console.WriteLine($"[ivf] dataset loaded: {dataset.Count} vectors.");

        var count = dataset.Count;
        var dims = IndexFileFormat.Dims;
        var clusters = Math.Max(1, options.ClusterCount);

        var centroids = InitializeCentroids(dataset.Vectors, count, dims, clusters, options.Seed);
        var assignments = new int[count];

        var iterations = Math.Max(1, options.Iterations);
        for (var iter = 0; iter < iterations; iter++)
        {
            Console.WriteLine($"[ivf] kmeans iter {iter + 1}/{iterations} started.");
            var sums = new float[clusters * dims];
            var counts = new int[clusters];

            for (var i = 0; i < count; i++)
            {
                if (i % ProgressEvery == 0)
                {
                    Console.WriteLine($"[ivf] iter {iter + 1}/{iterations} assignment {i}/{count}");
                }

                var vecOffset = i * dims;
                var best = 0;
                var bestDist = float.PositiveInfinity;
                for (var c = 0; c < clusters; c++)
                {
                    var centOffset = c * dims;
                    var dist = 0f;
                    for (var d = 0; d < dims; d++)
                    {
                        var diff = dataset.Vectors[vecOffset + d] - centroids[centOffset + d];
                        dist += diff * diff;
                    }
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = c;
                    }
                }

                assignments[i] = best;
                var sumOffset = best * dims;
                for (var d = 0; d < dims; d++)
                {
                    sums[sumOffset + d] += dataset.Vectors[vecOffset + d];
                }
                counts[best]++;
            }

            for (var c = 0; c < clusters; c++)
            {
                var centOffset = c * dims;
                var countC = counts[c];
                if (countC == 0)
                {
                    continue;
                }
                var inv = 1f / countC;
                for (var d = 0; d < dims; d++)
                {
                    centroids[centOffset + d] = sums[centOffset + d] * inv;
                }
            }

            Console.WriteLine($"[ivf] kmeans iter {iter + 1}/{iterations} finished.");
        }

        Console.WriteLine("[ivf] ordering vectors by cluster.");

        var order = Enumerable.Range(0, count).OrderBy(i => assignments[i]).ToArray();
        var offsets = new int[clusters + 1];
        var current = 0;
        for (var c = 0; c < clusters; c++)
        {
            offsets[c] = current;
            while (current < count && assignments[order[current]] == c)
            {
                current++;
            }
        }
        offsets[clusters] = count;

        var vectors = new short[count * dims];
        var labels = new byte[count];
        for (var idx = 0; idx < count; idx++)
        {
            if (idx % ProgressEvery == 0)
            {
                Console.WriteLine($"[ivf] quantizing {idx}/{count}");
            }

            var source = order[idx];
            var srcOffset = source * dims;
            var dstOffset = idx * dims;
            for (var d = 0; d < dims; d++)
            {
                vectors[dstOffset + d] = Quantization.QuantizeFloat(dataset.Vectors[srcOffset + d]);
            }
            labels[idx] = dataset.Labels[source] ? (byte)1 : (byte)0;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath) ?? ".");
        using var stream = File.Open(options.OutputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);

        WriteHeader(writer, dims, count, clusters);
        foreach (var value in centroids)
        {
            writer.Write(value);
        }
        foreach (var value in offsets)
        {
            writer.Write(value);
        }
        foreach (var value in vectors)
        {
            writer.Write(value);
        }
        writer.Write(labels);

        var elapsed = DateTime.UtcNow - startedAt;
        Console.WriteLine($"[ivf] done in {elapsed.TotalSeconds:F1}s: {options.OutputPath}");
    }

    private static float[] InitializeCentroids(float[] vectors, int count, int dims, int clusters, int seed)
    {
        var random = new Random(seed);
        var centroids = new float[clusters * dims];
        for (var c = 0; c < clusters; c++)
        {
            var idx = random.Next(0, count);
            var srcOffset = idx * dims;
            var dstOffset = c * dims;
            for (var d = 0; d < dims; d++)
            {
                centroids[dstOffset + d] = vectors[srcOffset + d];
            }
        }
        return centroids;
    }

    private static void WriteHeader(BinaryWriter writer, int dims, int count, int clusters)
    {
        var header = new byte[20];
        Array.Copy(System.Text.Encoding.ASCII.GetBytes(Magic), header, Magic.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), dims);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), count);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), clusters);
        writer.Write(header);
    }
}
