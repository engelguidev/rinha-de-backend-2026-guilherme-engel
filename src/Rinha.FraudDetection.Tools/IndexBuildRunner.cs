using Rinha.FraudDetection.Infrastructure.Index;

namespace Rinha.FraudDetection.Tools;

public sealed class IndexBuildRunner
{
    private readonly IndexBuildOptions _options;

    public IndexBuildRunner(IndexBuildOptions options)
    {
        _options = options;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var builder = new IndexBuilder();
        await builder.BuildAsync(_options.ReferencesFile, _options.OutputPath, cancellationToken);

        Console.WriteLine($"Index built at {_options.OutputPath}.");

        return 0;
    }
}
