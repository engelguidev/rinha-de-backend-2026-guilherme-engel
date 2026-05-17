using System;

namespace Rinha.FraudDetection.Domain.ValueObjects;

public readonly struct Vector14
{
    private readonly float[] _values;

    public Vector14(float[] values)
    {
        if (values is null || values.Length != 14)
        {
            throw new ArgumentException("Vector14 requires exactly 14 values.", nameof(values));
        }

        _values = values;
    }

    public ReadOnlySpan<float> Values => _values;

    public float this[int index] => _values[index];
}
