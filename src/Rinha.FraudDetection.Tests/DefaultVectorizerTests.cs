using Rinha.FraudDetection.Infrastructure.Vectorization;
using Rinha.FraudDetection.Application.Models;
using Rinha.FraudDetection.Domain.Models;
using Xunit;

namespace Rinha.FraudDetection.Tests;

public class DefaultVectorizerTests
{
    private readonly DefaultVectorizer _vectorizer = new();

    [Fact]
    public void Vectorize_WithValidPayload_ReturnsVector14()
    {
        var payload = new TransactionPayload(
            "tx-123",
            new TransactionInfo(100, 1, new DateTime(2026, 3, 11, 10, 30, 0, DateTimeKind.Utc)),
            new CustomerInfo(200, 5, new[] { "MERC-001" }),
            new MerchantInfo("MERC-001", "5411", 150),
            new TerminalInfo(false, true, 10),
            null
        );

        var normalization = new NormalizationConfig
        {
            MaxAmount = 10000,
            MaxInstallments = 12,
            AmountVsAvgRatio = 10,
            MaxMinutes = 1440,
            MaxKm = 1000,
            MaxTxCount24h = 20,
            MaxMerchantAvgAmount = 10000
        };

        var mccRisk = new MccRiskTable(new Dictionary<string, float> { { "5411", 0.3f } });

        var vector = _vectorizer.Vectorize(payload, normalization, mccRisk);

        Assert.NotNull(vector);
        Assert.Equal(14, vector.Values.Length);
    }

    [Fact]
    public void Vectorize_WithNullLastTransaction_SetsIndices5And6ToNegative1()
    {
        var payload = new TransactionPayload(
            "tx-123",
            new TransactionInfo(100, 1, new DateTime(2026, 3, 11, 10, 30, 0, DateTimeKind.Utc)),
            new CustomerInfo(200, 5, new[] { "MERC-001" }),
            new MerchantInfo("MERC-001", "5411", 150),
            new TerminalInfo(false, true, 10),
            null
        );

        var normalization = new NormalizationConfig
        {
            MaxAmount = 10000,
            MaxInstallments = 12,
            AmountVsAvgRatio = 10,
            MaxMinutes = 1440,
            MaxKm = 1000,
            MaxTxCount24h = 20,
            MaxMerchantAvgAmount = 10000
        };

        var mccRisk = new MccRiskTable(new Dictionary<string, float>());

        var vector = _vectorizer.Vectorize(payload, normalization, mccRisk);

        Assert.Equal(-1f, vector.Values[5]);
        Assert.Equal(-1f, vector.Values[6]);
    }

    [Fact]
    public void Vectorize_WithKnownMerchant_SetsIndex11ToZero()
    {
        var payload = new TransactionPayload(
            "tx-123",
            new TransactionInfo(100, 1, new DateTime(2026, 3, 11, 10, 30, 0, DateTimeKind.Utc)),
            new CustomerInfo(200, 5, new[] { "MERC-001" }),
            new MerchantInfo("MERC-001", "5411", 150),
            new TerminalInfo(false, true, 10),
            null
        );

        var normalization = new NormalizationConfig
        {
            MaxAmount = 10000,
            MaxInstallments = 12,
            AmountVsAvgRatio = 10,
            MaxMinutes = 1440,
            MaxKm = 1000,
            MaxTxCount24h = 20,
            MaxMerchantAvgAmount = 10000
        };

        var mccRisk = new MccRiskTable(new Dictionary<string, float>());

        var vector = _vectorizer.Vectorize(payload, normalization, mccRisk);

        Assert.Equal(0f, vector.Values[11]);
    }

    [Fact]
    public void Vectorize_WithUnknownMerchant_SetsIndex11ToOne()
    {
        var payload = new TransactionPayload(
            "tx-123",
            new TransactionInfo(100, 1, new DateTime(2026, 3, 11, 10, 30, 0, DateTimeKind.Utc)),
            new CustomerInfo(200, 5, new[] { "MERC-002" }),
            new MerchantInfo("MERC-001", "5411", 150),
            new TerminalInfo(false, true, 10),
            null
        );

        var normalization = new NormalizationConfig
        {
            MaxAmount = 10000,
            MaxInstallments = 12,
            AmountVsAvgRatio = 10,
            MaxMinutes = 1440,
            MaxKm = 1000,
            MaxTxCount24h = 20,
            MaxMerchantAvgAmount = 10000
        };

        var mccRisk = new MccRiskTable(new Dictionary<string, float>());

        var vector = _vectorizer.Vectorize(payload, normalization, mccRisk);

        Assert.Equal(1f, vector.Values[11]);
    }

    [Fact]
    public void Vectorize_AllValuesAreWithinNormalizedRange()
    {
        var payload = new TransactionPayload(
            "tx-123",
            new TransactionInfo(100, 1, new DateTime(2026, 3, 11, 10, 30, 0, DateTimeKind.Utc)),
            new CustomerInfo(200, 5, new[] { "MERC-001" }),
            new MerchantInfo("MERC-001", "5411", 150),
            new TerminalInfo(false, true, 10),
            null
        );

        var normalization = new NormalizationConfig
        {
            MaxAmount = 10000,
            MaxInstallments = 12,
            AmountVsAvgRatio = 10,
            MaxMinutes = 1440,
            MaxKm = 1000,
            MaxTxCount24h = 20,
            MaxMerchantAvgAmount = 10000
        };

        var mccRisk = new MccRiskTable(new Dictionary<string, float> { { "5411", 0.5f } });

        var vector = _vectorizer.Vectorize(payload, normalization, mccRisk);

        // Verifica que todos os valores (menos os sentinelas -1) estao em [0, 1]
        for (int i = 0; i < 14; i++)
        {
            if (i == 5 || i == 6) continue; // Skip sentinelas
            Assert.True(vector.Values[i] >= 0 && vector.Values[i] <= 1,
                $"Valor no indice {i} = {vector.Values[i]} nao esta em [0, 1]");
        }
    }

    [Fact]
    public void Vectorize_ExamplePayloadMatchesSpecVector()
    {
        var payload = new TransactionPayload(
            "tx-1329056812",
            new TransactionInfo(41.12, 2, new DateTime(2026, 3, 11, 18, 45, 53, DateTimeKind.Utc)),
            new CustomerInfo(82.24, 3, new[] { "MERC-003", "MERC-016" }),
            new MerchantInfo("MERC-016", "5411", 60.25),
            new TerminalInfo(false, true, 29.2331036248),
            null
        );

        var normalization = new NormalizationConfig
        {
            MaxAmount = 10000,
            MaxInstallments = 12,
            AmountVsAvgRatio = 10,
            MaxMinutes = 1440,
            MaxKm = 1000,
            MaxTxCount24h = 20,
            MaxMerchantAvgAmount = 10000
        };

        var mccRisk = new MccRiskTable(new Dictionary<string, float> { { "5411", 0.15f } });

        var vector = _vectorizer.Vectorize(payload, normalization, mccRisk);

        AssertClose(0.0041f, vector.Values[0]);
        AssertClose(0.1667f, vector.Values[1]);
        AssertClose(0.0500f, vector.Values[2]);
        AssertClose(0.7826f, vector.Values[3]);
        AssertClose(0.3333f, vector.Values[4]);
        Assert.Equal(-1f, vector.Values[5]);
        Assert.Equal(-1f, vector.Values[6]);
        AssertClose(0.0292f, vector.Values[7]);
        AssertClose(0.1500f, vector.Values[8]);
        Assert.Equal(0f, vector.Values[9]);
        Assert.Equal(1f, vector.Values[10]);
        Assert.Equal(0f, vector.Values[11]);
        AssertClose(0.1500f, vector.Values[12]);
        AssertClose(0.0060f, vector.Values[13]);
    }

    private static void AssertClose(float expected, float actual, float tolerance = 0.0005f)
    {
        Assert.True(Math.Abs(actual - expected) <= tolerance,
            $"Expected {expected} but got {actual}.");
    }
}
