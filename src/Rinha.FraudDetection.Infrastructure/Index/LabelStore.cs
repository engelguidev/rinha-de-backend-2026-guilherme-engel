using Rinha.FraudDetection.Application.Interfaces;

namespace Rinha.FraudDetection.Infrastructure.Index;

public sealed class LabelStore : ILabelStore
{
    private readonly bool[] _labels;

    public LabelStore(bool[] labels)
    {
        _labels = labels;
    }

    public bool IsFraud(int index)
    {
        if (index < 0 || index >= _labels.Length)
        {
            return false;
        }

        return _labels[index];
    }

    public static LabelStore Empty => new LabelStore(Array.Empty<bool>());
}
