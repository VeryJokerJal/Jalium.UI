namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a String property using key frames.
/// </summary>
public sealed class StringAnimationUsingKeyFrames : Timeline
{
    /// <summary>
    /// Gets the collection of key frames.
    /// </summary>
    public List<DiscreteStringKeyFrame> KeyFrames { get; } = new();
}

/// <summary>
/// A discrete key frame for string animation.
/// </summary>
public sealed class DiscreteStringKeyFrame
{
    /// <summary>
    /// Gets or sets the key time.
    /// </summary>
    public KeyTime KeyTime { get; set; }

    /// <summary>
    /// Gets or sets the target value.
    /// </summary>
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Animates the value of a Boolean property using key frames.
/// </summary>
public sealed class BooleanAnimationUsingKeyFrames : Timeline
{
    /// <summary>
    /// Gets the collection of key frames.
    /// </summary>
    public List<DiscreteBooleanKeyFrame> KeyFrames { get; } = new();
}

/// <summary>
/// A discrete key frame for boolean animation.
/// </summary>
public sealed class DiscreteBooleanKeyFrame
{
    /// <summary>
    /// Gets or sets the key time.
    /// </summary>
    public KeyTime KeyTime { get; set; }

    /// <summary>
    /// Gets or sets the target value.
    /// </summary>
    public bool Value { get; set; }
}

/// <summary>
/// Animates the value of a Char property using key frames.
/// </summary>
public sealed class CharAnimationUsingKeyFrames : Timeline
{
    /// <summary>
    /// Gets the collection of key frames.
    /// </summary>
    public List<DiscreteCharKeyFrame> KeyFrames { get; } = new();
}

/// <summary>
/// A discrete key frame for char animation.
/// </summary>
public sealed class DiscreteCharKeyFrame
{
    /// <summary>
    /// Gets or sets the key time.
    /// </summary>
    public KeyTime KeyTime { get; set; }

    /// <summary>
    /// Gets or sets the target value.
    /// </summary>
    public char Value { get; set; }
}
