using System.Collections;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// A discrete key frame for string animation.
/// </summary>
public class DiscreteStringKeyFrame : StringKeyFrame
{
    public DiscreteStringKeyFrame()
    {
    }

    public DiscreteStringKeyFrame(string value) => Value = value;

    public DiscreteStringKeyFrame(string value, KeyTime keyTime)
    {
        Value = value;
        KeyTime = keyTime;
    }

    protected override string InterpolateValueCore(string baseValue, double keyFrameProgress) =>
        keyFrameProgress >= 1.0 ? Value : baseValue;

    protected override Freezable CreateInstanceCore() => new DiscreteStringKeyFrame();
}
