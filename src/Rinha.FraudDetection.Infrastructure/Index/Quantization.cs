namespace Rinha.FraudDetection.Infrastructure.Index;

public static class Quantization
{
    public const short Scale = 10000;

    public static short QuantizeFloat(float value)
    {
        if (value <= -1f)
        {
            return (short)-Scale;
        }

        if (value <= 0f)
        {
            return 0;
        }

        if (value >= 1f)
        {
            return Scale;
        }

        return (short)Math.Round(value * Scale);
    }

    public static uint PartitionKey(ReadOnlySpan<short> v)
    {
        uint key = 0;
        if (v[5] > 0) key |= 1u << 0;
        if (v[9] > 0) key |= 1u << 1;
        if (v[10] > 0) key |= 1u << 2;
        if (v[11] > 0) key |= 1u << 3;

        if (v[12] <= 2047) {
        } else if (v[12] <= 4095) {
            key |= 1u << 4;
        } else if (v[12] <= 6143) {
            key |= 2u << 4;
        } else {
            key |= 3u << 4;
        }

        if (v[2] > 4096) key |= 1u << 6;
        if (v[8] > 2048) key |= 1u << 7;
        return key;
    }

    public static long LowerBound(ReadOnlySpan<short> q, ReadOnlySpan<short> min, ReadOnlySpan<short> max)
    {
        long sum = 0;
        for (var i = 0; i < IndexFileFormat.Dims; i++)
        {
            var diff = 0L;
            if (q[i] < min[i]) diff = min[i] - q[i];
            else if (q[i] > max[i]) diff = q[i] - max[i];
            sum += diff * diff;
        }
        return sum;
    }
}
