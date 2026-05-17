using Rinha.FraudDetection.Application.UseCases;
using Rinha.FraudDetection.Application.Interfaces;
using Rinha.FraudDetection.Application.Models;
using Rinha.FraudDetection.Domain.Models;
using Rinha.FraudDetection.Domain.ValueObjects;
using Xunit;
using Moq;

namespace Rinha.FraudDetection.Tests;

public class DetectFraudUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_WithFraudScore0_ReturnsApprovedTrue()
    {
        var payload = new TransactionPayload(
            "tx-123",
            new TransactionInfo(100, 1, new DateTime(2026, 3, 11, 10, 30, 0, DateTimeKind.Utc)),
            new CustomerInfo(200, 5, new[] { "MERC-001" }),
            new MerchantInfo("MERC-001", "5411", 150),
            new TerminalInfo(false, true, 10),
            null
        );

        var vectorizerMock = new Mock<IVectorizer>();
        var vector = new Vector14(new float[14]);
        vectorizerMock.Setup(v => v.Vectorize(It.IsAny<TransactionPayload>(), It.IsAny<NormalizationConfig>(), It.IsAny<MccRiskTable>()))
            .Returns(vector);

        var searchMock = new Mock<IVectorSearch>();
        searchMock.Setup(s => s.SearchAsync(It.IsAny<Vector14>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchOutcome(0, 5)); // 0 fraudes em 5 = score 0.0

        var resourceMock = new Mock<IResourceProvider>();
        resourceMock.Setup(r => r.GetNormalizationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NormalizationConfig { MaxAmount = 10000, MaxInstallments = 12, AmountVsAvgRatio = 10, MaxMinutes = 1440, MaxKm = 1000, MaxTxCount24h = 20, MaxMerchantAvgAmount = 10000 });
        resourceMock.Setup(r => r.GetMccRiskAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MccRiskTable(new Dictionary<string, float>()));

        var useCase = new DetectFraudUseCase(vectorizerMock.Object, searchMock.Object, resourceMock.Object);

        var result = await useCase.ExecuteAsync(payload, CancellationToken.None);

        Assert.True(result.Approved);
        Assert.Equal(0.0f, result.FraudScore);
    }

    [Fact]
    public async Task ExecuteAsync_WithFraudScore1_ReturnsApprovedFalse()
    {
        var payload = new TransactionPayload(
            "tx-123",
            new TransactionInfo(100, 1, new DateTime(2026, 3, 11, 10, 30, 0, DateTimeKind.Utc)),
            new CustomerInfo(200, 5, new[] { "MERC-001" }),
            new MerchantInfo("MERC-001", "5411", 150),
            new TerminalInfo(false, true, 10),
            null
        );

        var vectorizerMock = new Mock<IVectorizer>();
        var vector = new Vector14(new float[14]);
        vectorizerMock.Setup(v => v.Vectorize(It.IsAny<TransactionPayload>(), It.IsAny<NormalizationConfig>(), It.IsAny<MccRiskTable>()))
            .Returns(vector);

        var searchMock = new Mock<IVectorSearch>();
        searchMock.Setup(s => s.SearchAsync(It.IsAny<Vector14>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchOutcome(5, 5)); // 5 fraudes em 5 = score 1.0

        var resourceMock = new Mock<IResourceProvider>();
        resourceMock.Setup(r => r.GetNormalizationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NormalizationConfig { MaxAmount = 10000, MaxInstallments = 12, AmountVsAvgRatio = 10, MaxMinutes = 1440, MaxKm = 1000, MaxTxCount24h = 20, MaxMerchantAvgAmount = 10000 });
        resourceMock.Setup(r => r.GetMccRiskAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MccRiskTable(new Dictionary<string, float>()));

        var useCase = new DetectFraudUseCase(vectorizerMock.Object, searchMock.Object, resourceMock.Object);

        var result = await useCase.ExecuteAsync(payload, CancellationToken.None);

        Assert.False(result.Approved);
        Assert.Equal(1.0f, result.FraudScore);
    }

    [Fact]
    public async Task ExecuteAsync_WithFraudScoreExactly06_ReturnsApprovedTrue()
    {
        var payload = new TransactionPayload(
            "tx-123",
            new TransactionInfo(100, 1, new DateTime(2026, 3, 11, 10, 30, 0, DateTimeKind.Utc)),
            new CustomerInfo(200, 5, new[] { "MERC-001" }),
            new MerchantInfo("MERC-001", "5411", 150),
            new TerminalInfo(false, true, 10),
            null
        );

        var vectorizerMock = new Mock<IVectorizer>();
        var vector = new Vector14(new float[14]);
        vectorizerMock.Setup(v => v.Vectorize(It.IsAny<TransactionPayload>(), It.IsAny<NormalizationConfig>(), It.IsAny<MccRiskTable>()))
            .Returns(vector);

        var searchMock = new Mock<IVectorSearch>();
        searchMock.Setup(s => s.SearchAsync(It.IsAny<Vector14>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchOutcome(3, 5)); // 3 fraudes em 5 = score 0.6 (threshold)

        var resourceMock = new Mock<IResourceProvider>();
        resourceMock.Setup(r => r.GetNormalizationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NormalizationConfig { MaxAmount = 10000, MaxInstallments = 12, AmountVsAvgRatio = 10, MaxMinutes = 1440, MaxKm = 1000, MaxTxCount24h = 20, MaxMerchantAvgAmount = 10000 });
        resourceMock.Setup(r => r.GetMccRiskAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MccRiskTable(new Dictionary<string, float>()));

        var useCase = new DetectFraudUseCase(vectorizerMock.Object, searchMock.Object, resourceMock.Object);

        var result = await useCase.ExecuteAsync(payload, CancellationToken.None);

        // threshold == 0.6, entao score < 0.6 = false
        Assert.True(result.Approved);
        Assert.Equal(0.6f, result.FraudScore);
    }
}
