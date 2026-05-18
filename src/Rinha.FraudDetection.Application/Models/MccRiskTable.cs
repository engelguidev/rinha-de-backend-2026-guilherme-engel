using System.Collections.Generic;

namespace Rinha.FraudDetection.Application.Models;

public sealed class MccRiskTable
{
    private readonly IReadOnlyDictionary<string, float> _riskByMcc;
    private readonly uint[] _packedKeys;
    private readonly float[] _packedValues;
    private readonly int _packedCount;

    public MccRiskTable(IReadOnlyDictionary<string, float> riskByMcc, float defaultRisk = 0.5f)
    {
        _riskByMcc = riskByMcc;
        DefaultRisk = defaultRisk;
        _packedKeys = new uint[_riskByMcc.Count];
        _packedValues = new float[_riskByMcc.Count];
        var i = 0;
        foreach (var kv in _riskByMcc)
        {
            if (kv.Key.Length != 4)
            {
                continue;
            }

            var key = kv.Key;
            var packed = (uint)key[0]
                | ((uint)key[1] << 8)
                | ((uint)key[2] << 16)
                | ((uint)key[3] << 24);
            _packedKeys[i] = packed;
            _packedValues[i] = kv.Value;
            i++;
        }
        _packedCount = i;
    }

    public float DefaultRisk { get; }

    public float GetRisk(string mcc)
    {
        if (mcc is null)
        {
            return DefaultRisk;
        }

        return _riskByMcc.TryGetValue(mcc, out var risk) ? risk : DefaultRisk;
    }

    public float GetRisk(ReadOnlySpan<byte> mccBytes)
    {
        if (mccBytes.Length != 4)
        {
            return DefaultRisk;
        }

        var packed = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(mccBytes);
        for (var i = 0; i < _packedCount; i++)
        {
            if (_packedKeys[i] == packed)
            {
                return _packedValues[i];
            }
        }

        return DefaultRisk;
    }
}
