using System.Collections;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a String property using key frames.
/// </summary>
public partial class StringAnimationUsingKeyFrames : StringAnimationBase
{
    private StringKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of key frames.
    /// </summary>
    public StringKeyFrameCollection KeyFrames
    {
        get => _keyFrames;
        set => ReplaceAnimationChild(ref _keyFrames, value);
    }
}
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
