using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rinha.FraudDetection.Application.Interfaces;

namespace Rinha.FraudDetection.Infrastructure.Startup;

public sealed class IndexWarmupService : BackgroundService
{
    private readonly IVectorIndex _index;
    private readonly IResourceProvider _resourceProvider;
    private readonly IAppReadiness _readiness;
    private readonly ILogger<IndexWarmupService> _logger;

    public IndexWarmupService(
        IVectorIndex index,
        IResourceProvider resourceProvider,
        IAppReadiness readiness,
        ILogger<IndexWarmupService> logger)
    {
        _index = index;
        _resourceProvider = resourceProvider;
        _readiness = readiness;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _readiness.MarkNotReady();
        var delay = TimeSpan.FromMilliseconds(250);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _resourceProvider.GetNormalizationAsync(stoppingToken);
                await _resourceProvider.GetMccRiskAsync(stoppingToken);
                await _index.InitializeAsync(stoppingToken);

                _readiness.MarkReady();
                _logger.LogInformation("Warmup completed.");
                return;
            }
            catch (Exception ex)
            {
                _readiness.MarkNotReady();
                _logger.LogError(ex, "Warmup failed. Retrying in {DelayMs}ms.", delay.TotalMilliseconds);
            }

            await Task.Delay(delay, stoppingToken);
            var nextDelay = Math.Min(delay.TotalMilliseconds * 2, 5000);
            delay = TimeSpan.FromMilliseconds(nextDelay);
        }
    }
}
