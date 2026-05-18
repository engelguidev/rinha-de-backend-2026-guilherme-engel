using System.Text.Json;
using Rinha.FraudDetection.Application.Models;
using Rinha.FraudDetection.Presentation.Contracts;

namespace Rinha.FraudDetection.Presentation.Fast;

public sealed class FraudResponseCache
{
    private readonly byte[][] _bodies;
    private readonly int _k;

    public FraudResponseCache(FraudDetectionOptions options)
    {
        _k = Math.Max(1, options.KnnK);
        _bodies = BuildBodies(_k, options.FraudThreshold);
    }

    public ReadOnlyMemory<byte> BodyForScore(float score)
    {
        var idx = ScoreToIndex(score, _k);
        if ((uint)idx >= (uint)_bodies.Length)
        {
            idx = 0;
        }

        return _bodies[idx];
    }

    private static byte[][] BuildBodies(int k, float threshold)
    {
        var bodies = new byte[k + 1][];
        for (var n = 0; n <= k; n++)
        {
            var score = n / (float)k;
            var approved = score <= threshold;
            var response = new FraudScoreResponse(approved, score);
            bodies[n] = JsonSerializer.SerializeToUtf8Bytes(response);
        }

        return bodies;
    }

    private static int ScoreToIndex(float score, int k)
    {
        var idx = (int)MathF.Round(score * k);
        if (idx < 0)
        {
            return 0;
        }

        if (idx > k)
        {
            return k;
        }

        return idx;
    }
}
