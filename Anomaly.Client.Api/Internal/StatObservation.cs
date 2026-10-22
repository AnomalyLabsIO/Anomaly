namespace Anomaly.Client.Api.Internal;

/// <summary>Rebases samples when visibility or either role life changes.</summary>
internal sealed class StatObservation
{
    private bool _hasSample;
    private int _life;
    private int _viewerLife;
    private float _value;
    private float _maximum;

    internal bool Sample(int life, int viewerLife, bool observable, float value, float maximum,
        out float previous, out float previousMaximum)
    {
        previous = _value;
        previousMaximum = _maximum;
        if (!observable || !float.IsFinite(value) || !float.IsFinite(maximum))
        {
            _hasSample = false;
            return false;
        }

        bool changed = _hasSample && life == _life && viewerLife == _viewerLife &&
            (value != _value || maximum != _maximum);
        _hasSample = true;
        _life = life;
        _viewerLife = viewerLife;
        _value = value;
        _maximum = maximum;
        return changed;
    }
}
