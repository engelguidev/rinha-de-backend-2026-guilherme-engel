using Rinha.FraudDetection.Application.Models;

namespace Rinha.FraudDetection.Infrastructure.Vectorization;

public sealed class NormalizationRuntime
{
    public double MaxAmount { get; }
    public double MaxInstallments { get; }
    public double AmountVsAvgRatio { get; }
    public double MaxMinutes { get; }
    public double MaxKm { get; }
    public double MaxTxCount24h { get; }
    public double MaxMerchantAvgAmount { get; }

    public float InvMaxAmount { get; }
    public float InvMaxInstallments { get; }
    public float InvAmountVsAvgRatio { get; }
    public float InvMaxMinutes { get; }
    public float InvMaxKm { get; }
    public float InvMaxTxCount24h { get; }
    public float InvMaxMerchantAvgAmount { get; }

    public NormalizationRuntime(NormalizationConfig config)
    {
        MaxAmount = config.MaxAmount;
        MaxInstallments = config.MaxInstallments;
        AmountVsAvgRatio = config.AmountVsAvgRatio;
        MaxMinutes = config.MaxMinutes;
        MaxKm = config.MaxKm;
        MaxTxCount24h = config.MaxTxCount24h;
        MaxMerchantAvgAmount = config.MaxMerchantAvgAmount;

        InvMaxAmount = SafeInv(MaxAmount);
        InvMaxInstallments = SafeInv(MaxInstallments);
        InvAmountVsAvgRatio = SafeInv(AmountVsAvgRatio);
        InvMaxMinutes = SafeInv(MaxMinutes);
        InvMaxKm = SafeInv(MaxKm);
        InvMaxTxCount24h = SafeInv(MaxTxCount24h);
        InvMaxMerchantAvgAmount = SafeInv(MaxMerchantAvgAmount);
    }

    private static float SafeInv(double value)
    {
        if (value <= 0)
        {
            return 0f;
        }

        return (float)(1.0 / value);
    }
}
