using System.Text.Json.Serialization;
using Rinha.FraudDetection.Domain.Models;

namespace Rinha.FraudDetection.Presentation.Contracts;

public sealed class FraudScoreRequest
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("transaction")]
    public TransactionDto Transaction { get; init; } = new();

    [JsonPropertyName("customer")]
    public CustomerDto Customer { get; init; } = new();

    [JsonPropertyName("merchant")]
    public MerchantDto Merchant { get; init; } = new();

    [JsonPropertyName("terminal")]
    public TerminalDto Terminal { get; init; } = new();

    [JsonPropertyName("last_transaction")]
    public LastTransactionDto? LastTransaction { get; init; }

    public TransactionPayload ToDomain()
    {
        return new TransactionPayload(
            Id,
            new TransactionInfo(Transaction.Amount, Transaction.Installments, ToUtc(Transaction.RequestedAt)),
            new CustomerInfo(Customer.AvgAmount, Customer.TxCount24h, Customer.KnownMerchants),
            new MerchantInfo(Merchant.Id, Merchant.Mcc, Merchant.AvgAmount),
            new TerminalInfo(Terminal.IsOnline, Terminal.CardPresent, Terminal.KmFromHome),
            LastTransaction is null
                ? null
                : new LastTransactionInfo(ToUtc(LastTransaction.Timestamp), LastTransaction.KmFromCurrent)
        );
    }

    private static DateTime ToUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        if (value.Kind == DateTimeKind.Unspecified)
        {
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        return value.ToUniversalTime();
    }

    public sealed class TransactionDto
    {
        [JsonPropertyName("amount")]
        public double Amount { get; init; }

        [JsonPropertyName("installments")]
        public int Installments { get; init; }

        [JsonPropertyName("requested_at")]
        public DateTime RequestedAt { get; init; }
    }

    public sealed class CustomerDto
    {
        [JsonPropertyName("avg_amount")]
        public double AvgAmount { get; init; }

        [JsonPropertyName("tx_count_24h")]
        public int TxCount24h { get; init; }

        [JsonPropertyName("known_merchants")]
        public List<string> KnownMerchants { get; init; } = new();
    }

    public sealed class MerchantDto
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("mcc")]
        public string Mcc { get; init; } = string.Empty;

        [JsonPropertyName("avg_amount")]
        public double AvgAmount { get; init; }
    }

    public sealed class TerminalDto
    {
        [JsonPropertyName("is_online")]
        public bool IsOnline { get; init; }

        [JsonPropertyName("card_present")]
        public bool CardPresent { get; init; }

        [JsonPropertyName("km_from_home")]
        public double KmFromHome { get; init; }
    }

    public sealed class LastTransactionDto
    {
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; init; }

        [JsonPropertyName("km_from_current")]
        public double KmFromCurrent { get; init; }
    }
}
