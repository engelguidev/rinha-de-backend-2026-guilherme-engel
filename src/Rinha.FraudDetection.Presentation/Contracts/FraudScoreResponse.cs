using System.Text.Json.Serialization;

namespace Rinha.FraudDetection.Presentation.Contracts;

public sealed record FraudScoreResponse(
    [property: JsonPropertyName("approved")] bool Approved,
    [property: JsonPropertyName("fraud_score")] float FraudScore
);
