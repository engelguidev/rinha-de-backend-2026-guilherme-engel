using System.Text.Json;
using System.Text.Json.Serialization;
using Rinha.FraudDetection.Application.Interfaces;
using Rinha.FraudDetection.Application.Models;

namespace Rinha.FraudDetection.Infrastructure.Resources;

public sealed class JsonResourceProvider : IResourceProvider
{
    private readonly string _basePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private NormalizationConfig? _normalization;
    private MccRiskTable? _mccRisk;

    public JsonResourceProvider(string basePath)
    {
        _basePath = basePath;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<NormalizationConfig> GetNormalizationAsync(CancellationToken cancellationToken)
    {
        if (_normalization is not null)
        {
            return _normalization;
        }

        var path = Path.Combine(_basePath, "normalization.json");
        await using var stream = File.OpenRead(path);
        var data = await JsonSerializer.DeserializeAsync<NormalizationData>(stream, _jsonOptions, cancellationToken);

        if (data is null)
        {
            throw new InvalidOperationException("Unable to load normalization.json.");
        }

        _normalization = new NormalizationConfig
        {
            MaxAmount = data.MaxAmount,
            MaxInstallments = data.MaxInstallments,
            AmountVsAvgRatio = data.AmountVsAvgRatio,
            MaxMinutes = data.MaxMinutes,
            MaxKm = data.MaxKm,
            MaxTxCount24h = data.MaxTxCount24h,
            MaxMerchantAvgAmount = data.MaxMerchantAvgAmount
        };

        return _normalization;
    }

    public async Task<MccRiskTable> GetMccRiskAsync(CancellationToken cancellationToken)
    {
        if (_mccRisk is not null)
        {
            return _mccRisk;
        }

        var path = Path.Combine(_basePath, "mcc_risk.json");
        await using var stream = File.OpenRead(path);
        var data = await JsonSerializer.DeserializeAsync<Dictionary<string, float>>(stream, _jsonOptions, cancellationToken);

        if (data is null)
        {
            throw new InvalidOperationException("Unable to load mcc_risk.json.");
        }

        _mccRisk = new MccRiskTable(data);
        return _mccRisk;
    }

    private sealed class NormalizationData
    {
        [JsonPropertyName("max_amount")]
        public double MaxAmount { get; init; }

        [JsonPropertyName("max_installments")]
        public double MaxInstallments { get; init; }

        [JsonPropertyName("amount_vs_avg_ratio")]
        public double AmountVsAvgRatio { get; init; }

        [JsonPropertyName("max_minutes")]
        public double MaxMinutes { get; init; }

        [JsonPropertyName("max_km")]
        public double MaxKm { get; init; }

        [JsonPropertyName("max_tx_count_24h")]
        public double MaxTxCount24h { get; init; }

        [JsonPropertyName("max_merchant_avg_amount")]
        public double MaxMerchantAvgAmount { get; init; }
    }
}
