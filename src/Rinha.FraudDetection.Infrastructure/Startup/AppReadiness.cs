using Rinha.FraudDetection.Application.Interfaces;

namespace Rinha.FraudDetection.Infrastructure.Startup;

public sealed class AppReadiness : IAppReadiness
{
    private volatile bool _isReady;

    public bool IsReady => _isReady;

    public void MarkReady()
    {
        _isReady = true;
    }

    public void MarkNotReady()
    {
        _isReady = false;
    }
}
