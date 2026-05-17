using Rinha.FraudDetection.Application.Interfaces;
using Rinha.FraudDetection.Application.Models;
using Rinha.FraudDetection.Domain.Models;
using Rinha.FraudDetection.Domain.ValueObjects;

namespace Rinha.FraudDetection.Infrastructure.Vectorization;

public sealed class DefaultVectorizer : IVectorizer
{
    public Vector14 Vectorize(TransactionPayload payload, NormalizationConfig normalization, MccRiskTable mccRisk)
    {
        var values = new float[14];

        values[0] = Clamp(payload.Transaction.Amount / normalization.MaxAmount);
        values[1] = Clamp(payload.Transaction.Installments / normalization.MaxInstallments);

        var avg = payload.Customer.AvgAmount;
        var ratio = avg > 0 ? payload.Transaction.Amount / avg : normalization.AmountVsAvgRatio;
        values[2] = Clamp(ratio / normalization.AmountVsAvgRatio);

        var requestedAt = payload.Transaction.RequestedAtUtc;
        values[3] = (float)requestedAt.Hour / 23f;
        values[4] = (float)NormalizeDayOfWeek(requestedAt) / 6f;

        if (payload.LastTransaction is null)
        {
            values[5] = -1f;
            values[6] = -1f;
        }
        else
        {
            var minutes = (requestedAt - payload.LastTransaction.TimestampUtc).TotalMinutes;
            if (minutes < 0)
            {
                minutes = 0;
            }

            values[5] = Clamp(minutes / normalization.MaxMinutes);
            values[6] = Clamp(payload.LastTransaction.KmFromCurrent / normalization.MaxKm);
        }

        values[7] = Clamp(payload.Terminal.KmFromHome / normalization.MaxKm);
        values[8] = Clamp(payload.Customer.TxCount24h / normalization.MaxTxCount24h);
        values[9] = payload.Terminal.IsOnline ? 1f : 0f;
        values[10] = payload.Terminal.CardPresent ? 1f : 0f;
        values[11] = payload.Customer.KnownMerchants.Contains(payload.Merchant.Id) ? 0f : 1f;
        values[12] = mccRisk.GetRisk(payload.Merchant.Mcc);
        values[13] = Clamp(payload.Merchant.AvgAmount / normalization.MaxMerchantAvgAmount);

        return new Vector14(values);
    }

    private static float Clamp(double value)
    {
        if (value <= 0)
        {
            return 0f;
        }

        if (value >= 1)
        {
            return 1f;
        }

        return (float)value;
    }

    private static int NormalizeDayOfWeek(DateTime timestampUtc)
    {
        var day = (int)timestampUtc.DayOfWeek;
        return (day + 6) % 7;
    }
}
