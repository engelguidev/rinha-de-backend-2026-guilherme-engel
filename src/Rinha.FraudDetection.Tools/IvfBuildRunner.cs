namespace Rinha.FraudDetection.Tools;

public sealed class IvfBuildRunner
{
    private readonly IvfBuildOptions _options;

    public IvfBuildRunner(IvfBuildOptions options)
    {
        _options = options;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var builder = new IvfIndexBuilder();
        await builder.BuildAsync(_options, cancellationToken);
        Console.WriteLine($"IVF index built at {_options.OutputPath}.");
        return 0;
    }
}
