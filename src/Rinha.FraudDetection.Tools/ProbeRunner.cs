using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rinha.FraudDetection.Application.Interfaces;
using Rinha.FraudDetection.Application.Models;
using Rinha.FraudDetection.Domain.Models;
using Rinha.FraudDetection.Domain.ValueObjects;
using Rinha.FraudDetection.Infrastructure.Index;
using Rinha.FraudDetection.Infrastructure.Resources;
using Rinha.FraudDetection.Infrastructure.Vectorization;

namespace Rinha.FraudDetection.Tools;

public sealed class ProbeRunner
{
    private readonly ProbeOptions _options;

    public ProbeRunner(ProbeOptions options)
    {
        _options = options;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var payload = LoadPayload(_options.PayloadPath);
        if (payload is null)
        {
            Console.WriteLine("Unable to load payload.");
            return 1;
        }

        var resourceProvider = new JsonResourceProvider(_options.ResourcesPath);
        var normalization = await resourceProvider.GetNormalizationAsync(cancellationToken);
        var mccRisk = await resourceProvider.GetMccRiskAsync(cancellationToken);

        var vectorizer = new DefaultVectorizer();
        var vector = vectorizer.Vectorize(payload, normalization, mccRisk);

        Console.WriteLine("Vector (14):");
        Console.WriteLine(string.Join(", ", vector.Values.ToArray().Select(v => v.ToString("0.0000"))));

        var reader = new IndexFileReader(_options.IndexPath);
        reader.Load();

        Console.WriteLine($"Index count: {reader.Count}");
        var knn = SearchKnn(reader, vector, _options.K);
        Console.WriteLine("KNN labels (index, label, dist):");
        foreach (var item in knn)
        {
            Console.WriteLine($"{item.Index}\t{item.Label}\t{item.Distance}");
        }

        var fraudCount = knn.Count(item => item.Label > 0);
        Console.WriteLine($"fraud_score = {(float)fraudCount / knn.Count:0.0000} ({fraudCount}/{knn.Count})");

        if (!string.IsNullOrWhiteSpace(_options.ReferencesPath))
        {
            RunReferenceProbe(vector.Values, _options.ReferencesPath, _options.K);
        }

        return 0;
    }

    private static TransactionPayload? LoadPayload(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        PayloadDto? dto = null;
        if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
        {
            dto = JsonSerializer.Deserialize<PayloadDto>(doc.RootElement[0].GetRawText(), options);
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            dto = JsonSerializer.Deserialize<PayloadDto>(doc.RootElement.GetRawText(), options);
        }

        return dto?.ToDomain();
    }

    private static List<KnnItem> SearchKnn(IndexFileReader reader, Vector14 vector, int k)
    {
        Span<short> q = stackalloc short[IndexFileFormat.Dims];
        var values = vector.Values;
        for (var i = 0; i < IndexFileFormat.Dims; i++)
        {
            q[i] = Quantization.QuantizeFloat(values[i]);
        }

        var best = new KnnItem[k];
        for (var i = 0; i < k; i++)
        {
            best[i] = new KnnItem(-1, 0, long.MaxValue);
        }

        var partitions = reader.Partitions;
        var primaryKey = (int)Quantization.PartitionKey(q);
        if (primaryKey >= 0 && primaryKey < partitions.Length)
        {
            ScanPartition(reader, q, partitions[primaryKey], best);
        }

        var candidates = new List<(int Index, long Bound)>(partitions.Length);
        for (var i = 0; i < partitions.Length; i++)
        {
            if (i == primaryKey)
            {
                continue;
            }

            var bound = Quantization.LowerBound(q, partitions[i].Min, partitions[i].Max);
            if (bound < best[k - 1].Distance)
            {
                candidates.Add((i, bound));
            }
        }

        candidates.Sort((a, b) => a.Bound.CompareTo(b.Bound));
        for (var i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].Bound >= best[k - 1].Distance)
            {
                break;
            }

            ScanPartition(reader, q, partitions[candidates[i].Index], best);
        }

        return best.Where(item => item.Distance != long.MaxValue).ToList();
    }

    private static void ScanPartition(IndexFileReader reader, ReadOnlySpan<short> query,
        IndexFileReader.PartitionEntry partition, KnnItem[] best)
    {
        if (partition.Length <= 0)
        {
            return;
        }

        var vectors = reader.Vectors;
        var labels = reader.Labels;
        var start = partition.Start;
        var end = start + partition.Length;

        for (var i = start; i < end; i++)
        {
            var offset = i * IndexFileFormat.Dims;
            var dist = 0L;
            for (var d = 0; d < IndexFileFormat.Dims; d++)
            {
                var diff = (long)query[d] - vectors[offset + d];
                dist += diff * diff;
            }

            InsertBest(dist, labels[i], i, best);
        }
    }

    private static void InsertBest(long dist, byte label, int index, KnnItem[] best)
    {
        var k = best.Length;
        if (dist >= best[k - 1].Distance)
        {
            return;
        }

        var pos = k - 1;
        while (pos > 0 && dist < best[pos - 1].Distance)
        {
            best[pos] = best[pos - 1];
            pos--;
        }

        best[pos] = new KnnItem(index, label, dist);
    }

    private static void RunReferenceProbe(ReadOnlySpan<float> query, string referencesPath, int k)
    {
        if (!File.Exists(referencesPath))
        {
            Console.WriteLine("Reference file not found for probe.");
            return;
        }

        var json = File.ReadAllText(referencesPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var items = JsonSerializer.Deserialize<List<ReferenceItem>>(json, options) ?? new List<ReferenceItem>();

        var best = new List<(float Dist, string Label)>();
        foreach (var item in items)
        {
            if (item.Vector is null || item.Vector.Length != 14)
            {
                continue;
            }

            var dist = 0f;
            for (var i = 0; i < 14; i++)
            {
                var diff = query[i] - item.Vector[i];
                dist += diff * diff;
            }

            best.Add((dist, item.Label ?? ""));
        }

        var top = best.OrderBy(x => x.Dist).Take(k).ToArray();
        var fraudCount = top.Count(x => string.Equals(x.Label, "fraud", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"Sample refs fraud_score = {(float)fraudCount / top.Length:0.0000} ({fraudCount}/{top.Length})");
        foreach (var item in top)
        {
            Console.WriteLine($"ref\t{item.Label}\t{item.Dist:0.0000}");
        }
    }

    private sealed record KnnItem(int Index, byte Label, long Distance);

    private sealed class PayloadDto
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

    private sealed class ReferenceItem
    {
        [JsonPropertyName("vector")]
        public float[]? Vector { get; init; }

        [JsonPropertyName("label")]
        public string? Label { get; init; }
    }
}
