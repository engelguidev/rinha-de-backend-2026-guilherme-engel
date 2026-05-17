using Microsoft.Extensions.Hosting;
using Rinha.FraudDetection.Application.Interfaces;

namespace Rinha.FraudDetection.Infrastructure.Startup;

public sealed class IndexWarmupService : BackgroundService
{
    private readonly IVectorIndex _index;
    private readonly IResourceProvider _resourceProvider;
    private readonly IAppReadiness _readiness;

    public IndexWarmupService(IVectorIndex index, IResourceProvider resourceProvider, IAppReadiness readiness)
    {
        _index = index;
        _resourceProvider = resourceProvider;
        _readiness = readiness;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _readiness.MarkNotReady();

        await _resourceProvider.GetNormalizationAsync(stoppingToken);
        await _resourceProvider.GetMccRiskAsync(stoppingToken);
        await _index.InitializeAsync(stoppingToken);

        _readiness.MarkReady();
    }
}
