using System;
using System.Collections.Generic;

namespace Rinha.FraudDetection.Domain.Models;

public sealed record TransactionPayload(
    string Id,
    TransactionInfo Transaction,
    CustomerInfo Customer,
    MerchantInfo Merchant,
    TerminalInfo Terminal,
    LastTransactionInfo? LastTransaction
);

public sealed record TransactionInfo(
    double Amount,
    int Installments,
    DateTime RequestedAtUtc
);

public sealed record CustomerInfo(
    double AvgAmount,
    int TxCount24h,
    IReadOnlyList<string> KnownMerchants
);

public sealed record MerchantInfo(
    string Id,
    string Mcc,
    double AvgAmount
);

public sealed record TerminalInfo(
    bool IsOnline,
    bool CardPresent,
    double KmFromHome
);

public sealed record LastTransactionInfo(
    DateTime TimestampUtc,
    double KmFromCurrent
);

public sealed record FraudDecision(
    bool Approved,
    float FraudScore
);
