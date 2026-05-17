using System.Diagnostics;
using Rinha.FraudDetection.Infrastructure.Index;

namespace Rinha.FraudDetection.Tools;

public sealed class BenchmarkRunner
{
    private readonly BenchmarkOptions _options;

    public BenchmarkRunner(BenchmarkOptions options)
    {
        _options = options;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var loader = new ReferenceDatasetLoader(_options.ReferencesFile);
        var dataset = await loader.LoadAsync(cancellationToken);

        if (dataset.Count == 0)
        {
            Console.WriteLine("No vectors loaded.");
            return 1;
        }

        var lsh = new LshAnnIndex(loader, new LshIndexOptions());
        await lsh.InitializeAsync(cancellationToken);

        var random = new Random(_options.Seed);
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < _options.QueryCount; i++)
        {
            var index = random.Next(0, dataset.Count);
            var offset = index * 14;
            Span<float> query = stackalloc float[14];
            for (var d = 0; d < 14; d++)
            {
                query[d] = dataset.Vectors[offset + d];
            }

            var vector = new Rinha.FraudDetection.Domain.ValueObjects.Vector14(query.ToArray());
            _ = await lsh.SearchAsync(vector, 5, cancellationToken);
        }

        stopwatch.Stop();
        var avgMs = stopwatch.Elapsed.TotalMilliseconds / _options.QueryCount;
        Console.WriteLine($"Avg query time: {avgMs:F3} ms over {_options.QueryCount} queries.");

        return 0;
    }
}
