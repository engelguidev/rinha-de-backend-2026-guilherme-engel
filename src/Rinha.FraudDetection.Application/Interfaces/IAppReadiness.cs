namespace Rinha.FraudDetection.Application.Interfaces;

public interface IAppReadiness
{
    bool IsReady { get; }
    void MarkReady();
    void MarkNotReady();
}
