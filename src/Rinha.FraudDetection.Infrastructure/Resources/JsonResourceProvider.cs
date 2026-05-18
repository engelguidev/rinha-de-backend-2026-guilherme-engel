using Rinha.FraudDetection.Application.Interfaces;
using Rinha.FraudDetection.Application.Models;

namespace Rinha.FraudDetection.Infrastructure.Resources;

public sealed class JsonResourceProvider : IResourceProvider
{
    private NormalizationConfig? _normalization;
    private MccRiskTable? _mccRisk;

    public JsonResourceProvider(string basePath)
    {
        _ = basePath;
    }

    public Task<NormalizationConfig> GetNormalizationAsync(CancellationToken cancellationToken)
    {
        if (_normalization is not null)
        {
            return Task.FromResult(_normalization);
        }

        _normalization = BuildDefaultNormalization();

        return Task.FromResult(_normalization);
    }

    private static NormalizationConfig BuildDefaultNormalization()
    {
        return new NormalizationConfig
        {
            MaxAmount = 10000,
            MaxInstallments = 12,
            AmountVsAvgRatio = 10,
            MaxMinutes = 1440,
            MaxKm = 1000,
            MaxTxCount24h = 20,
            MaxMerchantAvgAmount = 10000
        };
    }

    public Task<MccRiskTable> GetMccRiskAsync(CancellationToken cancellationToken)
    {
        if (_mccRisk is not null)
        {
            return Task.FromResult(_mccRisk);
        }

        _mccRisk = BuildDefaultMccRisk();
        return Task.FromResult(_mccRisk);
    }

    private static MccRiskTable BuildDefaultMccRisk()
    {
        return new MccRiskTable(new Dictionary<string, float>
        {
            { "4511", 0.35f },
            { "5311", 0.25f },
            { "5411", 0.15f },
            { "5812", 0.30f },
            { "5912", 0.20f },
            { "5944", 0.45f },
            { "5999", 0.50f },
            { "7801", 0.80f },
            { "7802", 0.75f },
            { "7995", 0.85f }
        });
    }
}
