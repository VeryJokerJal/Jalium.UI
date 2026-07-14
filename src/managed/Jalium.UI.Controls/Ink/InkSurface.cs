namespace Jalium.UI.Ink;

using Jalium.UI.Input;

/// <summary>
/// Specifies an application gesture understood by an <see cref="Jalium.UI.Controls.InkCanvas"/>.
/// Numeric values match the WPF ink gesture identifiers so persisted settings remain portable.
/// </summary>
public enum ApplicationGesture
{
    AllGestures = 0,
    NoGesture = 0xF000,
    ScratchOut = 0xF001,
    Triangle = 0xF002,
    Square = 0xF003,
    Star = 0xF004,
    Check = 0xF005,
    Curlicue = 0xF010,
    DoubleCurlicue = 0xF011,
    Circle = 0xF020,
    DoubleCircle = 0xF021,
    SemicircleLeft = 0xF028,
    SemicircleRight = 0xF029,
    ChevronUp = 0xF030,
    ChevronDown = 0xF031,
    ChevronLeft = 0xF032,
    ChevronRight = 0xF033,
    ArrowUp = 0xF038,
    ArrowDown = 0xF039,
    ArrowLeft = 0xF03A,
    ArrowRight = 0xF03B,
    Up = 0xF058,
    Down = 0xF059,
    Left = 0xF05A,
    Right = 0xF05B,
    UpDown = 0xF060,
    DownUp = 0xF061,
    LeftRight = 0xF062,
    RightLeft = 0xF063,
    UpLeftLong = 0xF064,
    UpRightLong = 0xF065,
    DownLeftLong = 0xF066,
    DownRightLong = 0xF067,
    UpLeft = 0xF068,
    UpRight = 0xF069,
    DownLeft = 0xF06A,
    DownRight = 0xF06B,
    LeftUp = 0xF06C,
    LeftDown = 0xF06D,
    RightUp = 0xF06E,
    RightDown = 0xF06F,
    Exclamation = 0xF0A4,
    Tap = 0xF0F0,
    DoubleTap = 0xF0F1,
}

/// <summary>Specifies the confidence assigned to a recognized ink gesture.</summary>
public enum RecognitionConfidence
{
    Strong = 0,
    Intermediate = 1,
    Poor = 2,
}

/// <summary>Contains one gesture-recognition candidate.</summary>
public class GestureRecognitionResult
{
    internal GestureRecognitionResult(
        RecognitionConfidence recognitionConfidence,
        ApplicationGesture applicationGesture)
    {
        if (!Enum.IsDefined(recognitionConfidence))
            throw new ArgumentOutOfRangeException(nameof(recognitionConfidence));
        if (!Enum.IsDefined(applicationGesture))
            throw new ArgumentOutOfRangeException(nameof(applicationGesture));

        RecognitionConfidence = recognitionConfidence;
        ApplicationGesture = applicationGesture;
    }

    public RecognitionConfidence RecognitionConfidence { get; }

    public ApplicationGesture ApplicationGesture { get; }
}

/// <summary>Provides both values when an ink drawing-attributes instance is replaced.</summary>
public class DrawingAttributesReplacedEventArgs : EventArgs
{
    public DrawingAttributesReplacedEventArgs(
        DrawingAttributes newDrawingAttributes,
        DrawingAttributes previousDrawingAttributes)
    {
        NewDrawingAttributes = newDrawingAttributes
            ?? throw new ArgumentNullException(nameof(newDrawingAttributes));
        PreviousDrawingAttributes = previousDrawingAttributes
            ?? throw new ArgumentNullException(nameof(previousDrawingAttributes));
    }

    public DrawingAttributes NewDrawingAttributes { get; }

    public DrawingAttributes PreviousDrawingAttributes { get; }
}

/// <summary>Represents the method that handles drawing-attributes replacement.</summary>
public delegate void DrawingAttributesReplacedEventHandler(
    object sender,
    DrawingAttributesReplacedEventArgs e);

/// <summary>Provides old and new values for an ink property-data change.</summary>
public class PropertyDataChangedEventArgs : EventArgs
{
    public PropertyDataChangedEventArgs(Guid propertyGuid, object? newValue, object? previousValue)
    {
        if (newValue is null && previousValue is null)
            throw new ArgumentException("The new and previous values cannot both be null.");
        PropertyGuid = propertyGuid;
        NewValue = newValue;
        PreviousValue = previousValue;
    }

    public Guid PropertyGuid { get; }

    public object? NewValue { get; }

    public object? PreviousValue { get; }
}

/// <summary>Represents the method that handles an ink property-data change.</summary>
public delegate void PropertyDataChangedEventHandler(
    object sender,
    PropertyDataChangedEventArgs e);

/// <summary>Provides both collections when a stroke replaces its stylus points.</summary>
public class StylusPointsReplacedEventArgs : EventArgs
{
    public StylusPointsReplacedEventArgs(
        StylusPointCollection newStylusPoints,
        StylusPointCollection previousStylusPoints)
    {
        NewStylusPoints = newStylusPoints
            ?? throw new ArgumentNullException(nameof(newStylusPoints));
        PreviousStylusPoints = previousStylusPoints
            ?? throw new ArgumentNullException(nameof(previousStylusPoints));
    }

    public StylusPointCollection NewStylusPoints { get; }

    public StylusPointCollection PreviousStylusPoints { get; }
}

/// <summary>Represents the method that handles stylus-point replacement.</summary>
public delegate void StylusPointsReplacedEventHandler(
    object sender,
    StylusPointsReplacedEventArgs e);
