using System.Collections.ObjectModel;

namespace Jalium.UI.Ink;

/// <summary>
/// Recognizes application gestures in a collection of ink strokes.
/// </summary>
/// <remarks>
/// Jalium uses a managed, cross-platform recognizer. It currently recognizes taps
/// and the eight primary stroke directions; the public contract follows WPF even
/// though WPF delegates recognition to the Windows tablet recognizer.
/// </remarks>
public sealed class GestureRecognizer : DependencyObject, IDisposable
{
    private ApplicationGesture[] _enabledGestures;
    private bool _disposed;

    public GestureRecognizer()
        : this([ApplicationGesture.AllGestures])
    {
    }

    public GestureRecognizer(IEnumerable<ApplicationGesture> enabledApplicationGestures)
    {
        _enabledGestures = InkGestureRecognizerCore.ValidateEnabledGestures(enabledApplicationGestures);
    }

    public bool IsRecognizerAvailable
    {
        get
        {
            VerifyAccess();
            VerifyNotDisposed();
            return true;
        }
    }

    public void SetEnabledGestures(IEnumerable<ApplicationGesture> applicationGestures)
    {
        VerifyAccess();
        VerifyNotDisposed();
        _enabledGestures = InkGestureRecognizerCore.ValidateEnabledGestures(applicationGestures);
    }

    public ReadOnlyCollection<ApplicationGesture> GetEnabledGestures()
    {
        VerifyAccess();
        VerifyNotDisposed();
        return Array.AsReadOnly((ApplicationGesture[])_enabledGestures.Clone());
    }

    public ReadOnlyCollection<GestureRecognitionResult> Recognize(StrokeCollection strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        if (strokes.Count > 2)
            throw new ArgumentException("Gesture recognition accepts at most two strokes.", nameof(strokes));

        VerifyAccess();
        VerifyNotDisposed();

        var results = new List<GestureRecognitionResult>(capacity: 1);
        if (strokes.Count != 0 &&
            InkGestureRecognizerCore.Recognize(strokes[0], _enabledGestures) is { } result)
        {
            results.Add(result);
        }

        return new ReadOnlyCollection<GestureRecognitionResult>(results);
    }

    public void Dispose()
    {
        VerifyAccess();
        _disposed = true;
    }

    private void VerifyNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

internal static class InkGestureRecognizerCore
{
    internal static ApplicationGesture[] ValidateEnabledGestures(
        IEnumerable<ApplicationGesture> applicationGestures)
    {
        ArgumentNullException.ThrowIfNull(applicationGestures);

        var gestures = new List<ApplicationGesture>();
        foreach (ApplicationGesture gesture in applicationGestures)
        {
            if (!Enum.IsDefined(gesture))
                throw new ArgumentException("The specified ApplicationGesture is not valid.", nameof(applicationGestures));
            if (gestures.Contains(gesture))
                throw new ArgumentException("Duplicate ApplicationGesture values are not allowed.", nameof(applicationGestures));
            gestures.Add(gesture);
        }

        if (gestures.Count == 0)
            throw new ArgumentException("The ApplicationGesture collection must contain at least one member.", nameof(applicationGestures));
        if (gestures.Contains(ApplicationGesture.AllGestures) && gestures.Count != 1)
            throw new ArgumentException("AllGestures cannot be combined with another gesture.", nameof(applicationGestures));

        return gestures.ToArray();
    }

    internal static GestureRecognitionResult? Recognize(
        Stroke stroke,
        IReadOnlyCollection<ApplicationGesture> enabledGestures)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        ArgumentNullException.ThrowIfNull(enabledGestures);

        Input.StylusPointCollection points = stroke.StylusPoints;
        if (points.Count == 0)
            return null;

        Input.StylusPoint first = points[0];
        Input.StylusPoint last = points[points.Count - 1];
        double dx = last.X - first.X;
        double dy = last.Y - first.Y;
        double displacement = Math.Sqrt(dx * dx + dy * dy);
        double pathLength = 0;
        for (int i = 1; i < points.Count; i++)
        {
            double segmentX = points[i].X - points[i - 1].X;
            double segmentY = points[i].Y - points[i - 1].Y;
            pathLength += Math.Sqrt(segmentX * segmentX + segmentY * segmentY);
        }

        ApplicationGesture candidate;
        if (pathLength <= 20.0 && displacement <= 12.0)
        {
            candidate = ApplicationGesture.Tap;
        }
        else if (displacement < 10.0)
        {
            return null;
        }
        else if (Math.Abs(dx) >= Math.Abs(dy) * 2.0)
        {
            candidate = dx >= 0 ? ApplicationGesture.Right : ApplicationGesture.Left;
        }
        else if (Math.Abs(dy) >= Math.Abs(dx) * 2.0)
        {
            candidate = dy >= 0 ? ApplicationGesture.Down : ApplicationGesture.Up;
        }
        else if (dx >= 0)
        {
            candidate = dy >= 0 ? ApplicationGesture.DownRight : ApplicationGesture.UpRight;
        }
        else
        {
            candidate = dy >= 0 ? ApplicationGesture.DownLeft : ApplicationGesture.UpLeft;
        }

        bool allEnabled = enabledGestures.Count == 1 && enabledGestures.Contains(ApplicationGesture.AllGestures);
        if (!allEnabled && !enabledGestures.Contains(candidate))
            return null;

        double straightness = pathLength <= double.Epsilon ? 1.0 : displacement / pathLength;
        RecognitionConfidence confidence = straightness >= 0.85
            ? RecognitionConfidence.Strong
            : straightness >= 0.60
                ? RecognitionConfidence.Intermediate
                : RecognitionConfidence.Poor;
        return new GestureRecognitionResult(confidence, candidate);
    }
}
