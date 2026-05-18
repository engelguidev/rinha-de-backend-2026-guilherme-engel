using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Rinha.FraudDetection.Application.Interfaces;
using Rinha.FraudDetection.Application.Models;

namespace Rinha.FraudDetection.Infrastructure.Resources;

public sealed class JsonResourceProvider : IResourceProvider
{
    private readonly string _basePath;
    private readonly ILogger<JsonResourceProvider>? _logger;
    private NormalizationConfig? _normalization;
    private MccRiskTable? _mccRisk;

    public JsonResourceProvider(string basePath, ILogger<JsonResourceProvider>? logger = null)
    {
        _basePath = basePath;
        _logger = logger;
    }

    public Task<NormalizationConfig> GetNormalizationAsync(CancellationToken cancellationToken)
    {
        if (_normalization is not null)
        {
            return Task.FromResult(_normalization);
        }

        return LoadNormalizationAsync(cancellationToken);
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

        return LoadMccRiskAsync(cancellationToken);
    }

    private async Task<NormalizationConfig> LoadNormalizationAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_basePath, "normalization.json");
        if (!File.Exists(path))
        {
            _logger?.LogWarning("Normalization file not found at {Path}. Using defaults.", path);
            return _normalization ??= BuildDefaultNormalization();
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var dto = await JsonSerializer.DeserializeAsync<NormalizationDto>(stream, options, cancellationToken);
            if (dto is null)
            {
                _logger?.LogWarning("Normalization file at {Path} is empty. Using defaults.", path);
                return _normalization ??= BuildDefaultNormalization();
            }

            _normalization = new NormalizationConfig
            {
                MaxAmount = dto.MaxAmount,
                MaxInstallments = dto.MaxInstallments,
                AmountVsAvgRatio = dto.AmountVsAvgRatio,
                MaxMinutes = dto.MaxMinutes,
                MaxKm = dto.MaxKm,
                MaxTxCount24h = dto.MaxTxCount24h,
                MaxMerchantAvgAmount = dto.MaxMerchantAvgAmount
            };

            return _normalization;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load normalization file at {Path}. Using defaults.", path);
            return _normalization ??= BuildDefaultNormalization();
        }
    }

    private async Task<MccRiskTable> LoadMccRiskAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_basePath, "mcc_risk.json");
        if (!File.Exists(path))
        {
            _logger?.LogWarning("MCC risk file not found at {Path}. Using defaults.", path);
            return _mccRisk ??= BuildDefaultMccRisk();
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var map = await JsonSerializer.DeserializeAsync<Dictionary<string, float>>(stream, options, cancellationToken);
            if (map is null || map.Count == 0)
            {
                _logger?.LogWarning("MCC risk file at {Path} is empty. Using defaults.", path);
                return _mccRisk ??= BuildDefaultMccRisk();
            }

            _mccRisk = new MccRiskTable(map);
            return _mccRisk;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load MCC risk file at {Path}. Using defaults.", path);
            return _mccRisk ??= BuildDefaultMccRisk();
        }
    }

    private sealed class NormalizationDto
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
