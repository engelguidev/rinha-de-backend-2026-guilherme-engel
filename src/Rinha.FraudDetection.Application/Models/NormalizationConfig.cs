namespace Rinha.FraudDetection.Application.Models;

public sealed class NormalizationConfig
{
    public double MaxAmount { get; init; }
    public double MaxInstallments { get; init; }
    public double AmountVsAvgRatio { get; init; }
    public double MaxMinutes { get; init; }
    public double MaxKm { get; init; }
    public double MaxTxCount24h { get; init; }
    public double MaxMerchantAvgAmount { get; init; }
}
