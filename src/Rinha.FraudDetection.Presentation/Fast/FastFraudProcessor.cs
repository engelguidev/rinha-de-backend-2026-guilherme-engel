using System.Buffers;
using System.Collections.Generic;
using System.Text.Json;
using Rinha.FraudDetection.Application.Interfaces;
using Rinha.FraudDetection.Application.Models;
using Rinha.FraudDetection.Domain.ValueObjects;
using Rinha.FraudDetection.Infrastructure.Vectorization;

namespace Rinha.FraudDetection.Presentation.Fast;

public sealed class FastFraudProcessor
{
    private readonly IVectorSearch _search;
    private readonly IResourceProvider _resources;
    private readonly FraudDetectionOptions _options;
    private NormalizationRuntime? _normalization;
    private MccRiskTable? _mccRisk;
    private readonly object _initLock = new();

    public FastFraudProcessor(IVectorSearch search, IResourceProvider resources, FraudDetectionOptions options)
    {
        _search = search;
        _resources = resources;
        _options = options;
    }

    public float Score(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            return 0f;
        }

        EnsureResources();

        var normalization = _normalization;
        var mccRisk = _mccRisk;
        if (normalization is null || mccRisk is null)
        {
            return 0f;
        }

        try
        {
            if (!TryParse(body, normalization, mccRisk, out var vector))
            {
                return 0f;
            }

            var k = _options.KnnK <= 0 ? 5 : _options.KnnK;
            var outcome = _search.SearchAsync(vector, k, CancellationToken.None).GetAwaiter().GetResult();
            if (outcome.Total == 0)
            {
                return 0f;
            }

            return (float)outcome.FraudCount / outcome.Total;
        }
        catch (JsonException)
        {
            return 0f;
        }
    }

    public float Score(byte[] body)
    {
        return Score(body.AsSpan());
    }

    private void EnsureResources()
    {
        if (_normalization is not null && _mccRisk is not null)
        {
            return;
        }

        lock (_initLock)
        {
            if (_normalization is not null && _mccRisk is not null)
            {
                return;
            }

            var normalization = _resources.GetNormalizationAsync(CancellationToken.None).GetAwaiter().GetResult();
            var mccRisk = _resources.GetMccRiskAsync(CancellationToken.None).GetAwaiter().GetResult();

            _normalization ??= new NormalizationRuntime(normalization);
            _mccRisk ??= mccRisk;
        }
    }

    private static bool TryParse(ReadOnlySpan<byte> body, NormalizationRuntime norm, MccRiskTable mccRisk, out Vector14 vector)
    {
        vector = default;

        double amount = 0;
        int installments = 0;
        DateTime requestedAt = default;
        bool hasRequestedAt = false;
        double customerAvg = 0;
        int txCount24h = 0;
        double merchantAvg = 0;
        double kmFromHome = 0;
        bool isOnline = false;
        bool cardPresent = false;
        bool hasLast = false;
        DateTime lastTimestamp = default;
        double lastKm = 0;

        int merchantIdStart = -1;
        int merchantIdLen = 0;
        string? merchantIdString = null;
        int mccStart = -1;
        int mccLen = 0;
        string? mccString = null;
        int[] kmStarts = ArrayPool<int>.Shared.Rent(32);
        int[] kmLens = ArrayPool<int>.Shared.Rent(32);
        var kmCount = 0;
        List<string>? kmOverflowStrings = null;
        bool knownMerchant = false;

        try
        {
            var reader = new Utf8JsonReader(body, isFinalBlock: true, state: default);
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                if (reader.ValueTextEquals("transaction"u8))
                {
                    if (!ReadTransaction(ref reader, ref amount, ref installments, ref requestedAt, ref hasRequestedAt))
                    {
                        return false;
                    }
                    continue;
                }

                if (reader.ValueTextEquals("customer"u8))
                {
                    if (!ReadCustomer(ref reader, body, ref customerAvg, ref txCount24h,
                            kmStarts, kmLens, ref kmCount, ref kmOverflowStrings,
                            merchantIdStart, merchantIdLen, merchantIdString, ref knownMerchant))
                    {
                        return false;
                    }
                    continue;
                }

                if (reader.ValueTextEquals("merchant"u8))
                {
                    if (!ReadMerchant(ref reader, body, ref merchantAvg, ref merchantIdStart, ref merchantIdLen, ref merchantIdString, ref mccStart, ref mccLen, ref mccString))
                    {
                        return false;
                    }
                    continue;
                }

                if (reader.ValueTextEquals("terminal"u8))
                {
                    if (!ReadTerminal(ref reader, ref isOnline, ref cardPresent, ref kmFromHome))
                    {
                        return false;
                    }
                    continue;
                }

                if (reader.ValueTextEquals("last_transaction"u8))
                {
                    if (!ReadLast(ref reader, ref hasLast, ref lastTimestamp, ref lastKm))
                    {
                        return false;
                    }
                }
            }

            if (!knownMerchant && merchantIdStart >= 0 && merchantIdLen > 0)
            {
                var merchantId = body.Slice(merchantIdStart, merchantIdLen);
                if (kmCount > 0)
                {
                    for (var i = 0; i < kmCount; i++)
                    {
                        if (merchantId.SequenceEqual(body.Slice(kmStarts[i], kmLens[i])))
                        {
                            knownMerchant = true;
                            break;
                        }
                    }
                }
            }

            if (!knownMerchant && merchantIdString is not null)
            {
                var merchantIdBytes = System.Text.Encoding.UTF8.GetBytes(merchantIdString);
                if (kmCount > 0)
                {
                    for (var i = 0; i < kmCount; i++)
                    {
                        if (merchantIdBytes.AsSpan().SequenceEqual(body.Slice(kmStarts[i], kmLens[i])))
                        {
                            knownMerchant = true;
                            break;
                        }
                    }
                }

                if (!knownMerchant && kmOverflowStrings is not null)
                {
                    for (var i = 0; i < kmOverflowStrings.Count; i++)
                    {
                        if (string.Equals(merchantIdString, kmOverflowStrings[i], StringComparison.Ordinal))
                        {
                            knownMerchant = true;
                            break;
                        }
                    }
                }
            }

            if (!hasRequestedAt)
            {
                return false;
            }

            var values = new float[14];
            values[0] = Clamp(amount * norm.InvMaxAmount);
            values[1] = Clamp(installments * norm.InvMaxInstallments);
            var ratio = customerAvg > 0 ? (amount / customerAvg) * norm.InvAmountVsAvgRatio : 0.0;
            values[2] = Clamp(ratio);

            values[3] = requestedAt.Hour / 23f;
            values[4] = NormalizeDayOfWeek(requestedAt) / 6f;

            if (hasLast)
            {
                var minutes = (requestedAt - lastTimestamp).TotalMinutes;
                if (minutes < 0)
                {
                    minutes = 0;
                }
                values[5] = Clamp(minutes * norm.InvMaxMinutes);
                values[6] = Clamp(lastKm * norm.InvMaxKm);
            }
            else
            {
                values[5] = -1f;
                values[6] = -1f;
            }

            values[7] = Clamp(kmFromHome * norm.InvMaxKm);
            values[8] = Clamp(txCount24h * norm.InvMaxTxCount24h);
            values[9] = isOnline ? 1f : 0f;
            values[10] = cardPresent ? 1f : 0f;
            values[11] = knownMerchant ? 0f : 1f;
            values[12] = mccStart >= 0 && mccLen == 4
                ? mccRisk.GetRisk(body.Slice(mccStart, mccLen))
                : mccString is not null
                    ? mccRisk.GetRisk(mccString)
                    : mccRisk.DefaultRisk;
            values[13] = Clamp(merchantAvg * norm.InvMaxMerchantAvgAmount);

            vector = new Vector14(values);
            return true;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(kmStarts, clearArray: false);
            ArrayPool<int>.Shared.Return(kmLens, clearArray: false);
        }
    }

    private static bool ReadTransaction(ref Utf8JsonReader reader, ref double amount, ref int installments, ref DateTime requestedAt, ref bool hasRequestedAt)
    {
        if (!ReadStartObject(ref reader))
        {
            return false;
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return true;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            if (reader.ValueTextEquals("amount"u8))
            {
                reader.Read();
                amount = reader.GetDouble();
            }
            else if (reader.ValueTextEquals("installments"u8))
            {
                reader.Read();
                installments = reader.GetInt32();
            }
            else if (reader.ValueTextEquals("requested_at"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.String && TryParseIsoUtc(reader.ValueSpan, out var parsed))
                {
                    requestedAt = parsed;
                    hasRequestedAt = true;
                }
                else if (reader.TokenType == JsonTokenType.String)
                {
                    if (DateTime.TryParse(reader.GetString(), out parsed))
                    {
                        requestedAt = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                        hasRequestedAt = true;
                    }
                }
            }
            else
            {
                reader.Read();
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    reader.Skip();
                }
            }
        }

        return false;
    }

    private static bool ReadCustomer(ref Utf8JsonReader reader, ReadOnlySpan<byte> body, ref double avgAmount, ref int txCount24h,
        int[] kmStarts, int[] kmLens, ref int kmCount, ref List<string>? kmOverflowStrings,
        int merchantIdStart, int merchantIdLen, string? merchantIdString, ref bool knownMerchant)
    {
        if (!ReadStartObject(ref reader))
        {
            return false;
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return true;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            if (reader.ValueTextEquals("avg_amount"u8))
            {
                reader.Read();
                avgAmount = reader.GetDouble();
            }
            else if (reader.ValueTextEquals("tx_count_24h"u8))
            {
                reader.Read();
                txCount24h = reader.GetInt32();
            }
            else if (reader.ValueTextEquals("known_merchants"u8))
            {
                reader.Read();
                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    return false;
                }

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        break;
                    }

                    if (reader.TokenType != JsonTokenType.String)
                    {
                        continue;
                    }

                    if (!reader.ValueIsEscaped && merchantIdStart >= 0 && merchantIdLen > 0)
                    {
                        var start = (int)reader.TokenStartIndex + 1;
                        var len = reader.ValueSpan.Length;
                        if (len == merchantIdLen && body.Slice(merchantIdStart, merchantIdLen).SequenceEqual(body.Slice(start, len)))
                        {
                            knownMerchant = true;
                        }
                    }
                    else if (reader.ValueIsEscaped && merchantIdString is not null)
                    {
                        var value = reader.GetString();
                        if (value is not null && string.Equals(value, merchantIdString, StringComparison.Ordinal))
                        {
                            knownMerchant = true;
                        }
                    }

                    if (knownMerchant)
                    {
                        continue;
                    }

                    if (reader.ValueIsEscaped)
                    {
                        var value = reader.GetString();
                        if (value is null)
                        {
                            continue;
                        }

                        kmOverflowStrings ??= new List<string>();
                        kmOverflowStrings.Add(value);
                        continue;
                    }

                    var entryStart = (int)reader.TokenStartIndex + 1;
                    var entryLen = reader.ValueSpan.Length;
                    if (kmCount < kmStarts.Length)
                    {
                        kmStarts[kmCount] = entryStart;
                        kmLens[kmCount] = entryLen;
                        kmCount++;
                    }
                }
            }
            else
            {
                reader.Read();
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    reader.Skip();
                }
            }
        }

        return false;
    }

    private static bool ReadMerchant(ref Utf8JsonReader reader, ReadOnlySpan<byte> body, ref double avgAmount,
        ref int merchantIdStart, ref int merchantIdLen, ref string? merchantIdString,
        ref int mccStart, ref int mccLen, ref string? mccString)
    {
        if (!ReadStartObject(ref reader))
        {
            return false;
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return true;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            if (reader.ValueTextEquals("id"u8))
            {
                reader.Read();
                if (!reader.ValueIsEscaped && reader.TokenType == JsonTokenType.String)
                {
                    merchantIdStart = (int)reader.TokenStartIndex + 1;
                    merchantIdLen = reader.ValueSpan.Length;
                }
                else if (reader.TokenType == JsonTokenType.String)
                {
                    merchantIdString = reader.GetString();
                }
            }
            else if (reader.ValueTextEquals("mcc"u8))
            {
                reader.Read();
                if (!reader.ValueIsEscaped && reader.TokenType == JsonTokenType.String)
                {
                    mccStart = (int)reader.TokenStartIndex + 1;
                    mccLen = reader.ValueSpan.Length;
                }
                else if (reader.TokenType == JsonTokenType.String)
                {
                    mccString = reader.GetString();
                }
            }
            else if (reader.ValueTextEquals("avg_amount"u8))
            {
                reader.Read();
                avgAmount = reader.GetDouble();
            }
            else
            {
                reader.Read();
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    reader.Skip();
                }
            }
        }

        return false;
    }

    private static bool ReadTerminal(ref Utf8JsonReader reader, ref bool isOnline, ref bool cardPresent, ref double kmFromHome)
    {
        if (!ReadStartObject(ref reader))
        {
            return false;
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return true;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            if (reader.ValueTextEquals("is_online"u8))
            {
                reader.Read();
                isOnline = reader.TokenType == JsonTokenType.True;
            }
            else if (reader.ValueTextEquals("card_present"u8))
            {
                reader.Read();
                cardPresent = reader.TokenType == JsonTokenType.True;
            }
            else if (reader.ValueTextEquals("km_from_home"u8))
            {
                reader.Read();
                kmFromHome = reader.GetDouble();
            }
            else
            {
                reader.Read();
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    reader.Skip();
                }
            }
        }

        return false;
    }

    private static bool ReadLast(ref Utf8JsonReader reader, ref bool hasLast, ref DateTime lastTimestamp, ref double lastKm)
    {
        reader.Read();
        if (reader.TokenType == JsonTokenType.Null)
        {
            hasLast = false;
            return true;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        hasLast = true;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return true;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            if (reader.ValueTextEquals("timestamp"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.String && TryParseIsoUtc(reader.ValueSpan, out var parsed))
                {
                    lastTimestamp = parsed;
                }
                else if (reader.TokenType == JsonTokenType.String)
                {
                    if (DateTime.TryParse(reader.GetString(), out parsed))
                    {
                        lastTimestamp = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                    }
                }
            }
            else if (reader.ValueTextEquals("km_from_current"u8))
            {
                reader.Read();
                lastKm = reader.GetDouble();
            }
            else
            {
                reader.Read();
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    reader.Skip();
                }
            }
        }

        return false;
    }

    private static bool ReadStartObject(ref Utf8JsonReader reader)
    {
        reader.Read();
        return reader.TokenType == JsonTokenType.StartObject;
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

    private static bool TryParseIsoUtc(ReadOnlySpan<byte> value, out DateTime timestamp)
    {
        timestamp = default;
        if (value.Length < 19)
        {
            return false;
        }

        var year = Parse4(value, 0);
        var month = Parse2(value, 5);
        var day = Parse2(value, 8);
        var hour = Parse2(value, 11);
        var minute = Parse2(value, 14);
        var second = Parse2(value, 17);
        if (year <= 0 || month <= 0 || day <= 0)
        {
            return false;
        }

        try
        {
            timestamp = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int Parse2(ReadOnlySpan<byte> value, int start)
    {
        var tens = value[start] - (byte)'0';
        var ones = value[start + 1] - (byte)'0';
        if ((uint)tens > 9 || (uint)ones > 9)
        {
            return -1;
        }

        return tens * 10 + ones;
    }

    private static int Parse4(ReadOnlySpan<byte> value, int start)
    {
        var a = value[start] - (byte)'0';
        var b = value[start + 1] - (byte)'0';
        var c = value[start + 2] - (byte)'0';
        var d = value[start + 3] - (byte)'0';
        if ((uint)a > 9 || (uint)b > 9 || (uint)c > 9 || (uint)d > 9)
        {
            return -1;
        }

        return a * 1000 + b * 100 + c * 10 + d;
    }
}
