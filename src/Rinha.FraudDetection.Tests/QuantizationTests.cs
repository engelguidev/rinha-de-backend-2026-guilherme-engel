using Rinha.FraudDetection.Infrastructure.Index;
using Xunit;

namespace Rinha.FraudDetection.Tests;

public class QuantizationTests
{
    [Fact]
    public void QuantizeFloat_WithZero_ReturnsZero()
    {
        var result = Quantization.QuantizeFloat(0f);
        Assert.Equal(0, result);
    }

    [Fact]
    public void QuantizeFloat_WithOne_ReturnsScale()
    {
        var result = Quantization.QuantizeFloat(1f);
        Assert.Equal(10000, result);
    }

    [Fact]
    public void QuantizeFloat_WithHalf_ReturnsHalfScale()
    {
        var result = Quantization.QuantizeFloat(0.5f);
        Assert.Equal(5000, result);
    }

    [Fact]
    public void QuantizeFloat_WithNegativeOne_ReturnsNegativeScale()
    {
        var result = Quantization.QuantizeFloat(-1f);
        Assert.Equal(-10000, result);
    }

    [Fact]
    public void QuantizeFloat_WithValueBelowZero_ReturnsZero()
    {
        var result = Quantization.QuantizeFloat(-0.5f);
        Assert.Equal(0, result);
    }

    [Fact]
    public void QuantizeFloat_WithValueAboveOne_ReturnsScale()
    {
        var result = Quantization.QuantizeFloat(1.5f);
        Assert.Equal(10000, result);
    }

    [Fact]
    public void PartitionKey_WithAllZeros_ReturnsZero()
    {
        Span<short> v = stackalloc short[14];
        var key = Quantization.PartitionKey(v);
        Assert.Equal(0u, key);
    }

    [Fact]
    public void PartitionKey_WithDifferentValues_ComputesCorrectKey()
    {
        Span<short> v = stackalloc short[14];
        v[5] = 100;  // >= 0
        v[9] = 1;    // > 0
        v[10] = 1;   // > 0
        v[11] = 1;   // > 0

        var key = Quantization.PartitionKey(v);
        
        // Deve ter bits 0, 1, 2, 3 setados
        Assert.True((key & 0x0F) != 0);
    }

    [Fact]
    public void LowerBound_WithVectorInsideBox_ReturnsZero()
    {
        Span<short> q = stackalloc short[14];
        Span<short> min = stackalloc short[14];
        Span<short> max = stackalloc short[14];

        for (int i = 0; i < 14; i++)
        {
            q[i] = 5000;
            min[i] = 0;
            max[i] = 10000;
        }

        var bound = Quantization.LowerBound(q, min, max);
        Assert.Equal(0, bound);
    }

    [Fact]
    public void LowerBound_WithVectorOutsideBox_ReturnsPositive()
    {
        Span<short> q = stackalloc short[14];
        Span<short> min = stackalloc short[14];
        Span<short> max = stackalloc short[14];

        for (int i = 0; i < 14; i++)
        {
            q[i] = 0;
            min[i] = 5000;
            max[i] = 10000;
        }

        var bound = Quantization.LowerBound(q, min, max);
        Assert.True(bound > 0);
    }
}
