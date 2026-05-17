using System.Collections.Generic;

namespace Rinha.FraudDetection.Application.Models;

public sealed class MccRiskTable
{
    private readonly IReadOnlyDictionary<string, float> _riskByMcc;

    public MccRiskTable(IReadOnlyDictionary<string, float> riskByMcc, float defaultRisk = 0.5f)
    {
        _riskByMcc = riskByMcc;
        DefaultRisk = defaultRisk;
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
}
