using Jalium.UI;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Specifies the output property of a path that is used to animate a value.
/// </summary>
public enum PathAnimationSource
{
    /// <summary>
    /// The X coordinate of the path.
    /// </summary>
    X,

    /// <summary>
    /// The Y coordinate of the path.
    /// </summary>
    Y,

    /// <summary>
    /// The angle of the tangent at the point on the path.
    /// </summary>
    Angle
}

/// <summary>
/// Animates a Double value along a PathGeometry.
/// </summary>
public sealed class DoubleAnimationUsingPath : AnimationTimeline<double>
{
    private bool _isValid;
    private double _accumulatingValue;

    /// <summary>
    /// Gets or sets the PathGeometry that defines the path.
    /// </summary>
    public PathGeometry? PathGeometry { get; set; }

    /// <summary>
    /// Gets or sets the source of the animation value (X, Y, or Angle).
    /// </summary>
    public PathAnimationSource Source { get; set; } = PathAnimationSource.X;

    /// <summary>
    /// Gets or sets whether the animation is additive.
    /// </summary>
    public new bool IsAdditive { get; set; }

    /// <summary>
    /// Gets or sets whether the animation is cumulative across repeats.
    /// </summary>
    public new bool IsCumulative { get; set; }

    /// <inheritdoc />
    protected override double GetCurrentValueCore(double defaultOriginValue, double defaultDestinationValue, AnimationClock animationClock)
    {
        var pathGeometry = PathGeometry;
        if (pathGeometry == null)
        {
            return defaultDestinationValue;
        }

        if (!_isValid)
        {
            Validate();
        }

        pathGeometry.GetPointAtFractionLength(animationClock.CurrentProgress, out var pathPoint, out var pathTangent);

        double pathValue = Source switch
        {
            PathAnimationSource.X => pathPoint.X,
            PathAnimationSource.Y => pathPoint.Y,
            PathAnimationSource.Angle => CalculateAngleFromTangentVector(pathTangent.X, pathTangent.Y),
            _ => 0
        };

        // Handle cumulative behavior
        if (IsCumulative && animationClock.CurrentTime.HasValue)
        {
            var duration = Duration.HasTimeSpan ? Duration.TimeSpan.TotalMilliseconds : 1000;
            var currentRepeat = Math.Floor(animationClock.CurrentTime.Value.TotalMilliseconds / duration);
            if (currentRepeat > 0)
            {
                pathValue += _accumulatingValue * currentRepeat;
            }
        }

        if (IsAdditive)
        {
            return defaultOriginValue + pathValue;
        }

        return pathValue;
    }

    private void Validate()
    {
        if (IsCumulative && PathGeometry != null)
        {
            PathGeometry.GetPointAtFractionLength(0.0, out var startPoint, out var startTangent);
            PathGeometry.GetPointAtFractionLength(1.0, out var endPoint, out var endTangent);

            _accumulatingValue = Source switch
            {
                PathAnimationSource.X => endPoint.X - startPoint.X,
                PathAnimationSource.Y => endPoint.Y - startPoint.Y,
                PathAnimationSource.Angle => CalculateAngleFromTangentVector(endTangent.X, endTangent.Y) -
                                            CalculateAngleFromTangentVector(startTangent.X, startTangent.Y),
                _ => 0
            };
        }

        _isValid = true;
    }

    internal static double CalculateAngleFromTangentVector(double x, double y)
    {
        var angle = Math.Acos(Math.Clamp(x, -1, 1)) * (180.0 / Math.PI);
        if (y < 0.0)
        {
            angle = 360 - angle;
        }
        return angle;
    }
}

/// <summary>
/// Animates a Point value along a PathGeometry.
/// </summary>
public sealed class PointAnimationUsingPath : AnimationTimeline<Point>
{
    private bool _isValid;
    private Vector _accumulatingVector;

    /// <summary>
    /// Gets or sets the PathGeometry that defines the path.
    /// </summary>
    public PathGeometry? PathGeometry { get; set; }

    /// <summary>
    /// Gets or sets whether the animation is additive.
    /// </summary>
    public new bool IsAdditive { get; set; }

    /// <summary>
    /// Gets or sets whether the animation is cumulative across repeats.
    /// </summary>
    public new bool IsCumulative { get; set; }

    /// <inheritdoc />
    protected override Point GetCurrentValueCore(Point defaultOriginValue, Point defaultDestinationValue, AnimationClock animationClock)
    {
        var pathGeometry = PathGeometry;
        if (pathGeometry == null)
        {
            return defaultDestinationValue;
        }

        if (!_isValid)
        {
            Validate();
        }

        pathGeometry.GetPointAtFractionLength(animationClock.CurrentProgress, out var pathPoint, out _);

        // Handle cumulative behavior
        if (IsCumulative && animationClock.CurrentTime.HasValue)
        {
            var duration = Duration.HasTimeSpan ? Duration.TimeSpan.TotalMilliseconds : 1000;
            var currentRepeat = Math.Floor(animationClock.CurrentTime.Value.TotalMilliseconds / duration);
            if (currentRepeat > 0)
            {
                pathPoint = new Point(
                    pathPoint.X + _accumulatingVector.X * currentRepeat,
                    pathPoint.Y + _accumulatingVector.Y * currentRepeat
                );
            }
        }

        if (IsAdditive)
        {
            return new Point(
                defaultOriginValue.X + pathPoint.X,
                defaultOriginValue.Y + pathPoint.Y
            );
        }

        return pathPoint;
    }

    private void Validate()
    {
        if (IsCumulative && PathGeometry != null)
        {
            PathGeometry.GetPointAtFractionLength(0.0, out var startPoint, out _);
            PathGeometry.GetPointAtFractionLength(1.0, out var endPoint, out _);

            _accumulatingVector = new Vector(
                endPoint.X - startPoint.X,
                endPoint.Y - startPoint.Y
            );
        }

        _isValid = true;
    }
}

/// <summary>
/// Animates a Matrix value along a PathGeometry.
/// </summary>
public sealed class MatrixAnimationUsingPath : AnimationTimeline<Matrix>
{
    private bool _isValid;
    private double _accumulatingAngle;
    private Vector _accumulatingOffset;

    /// <summary>
    /// Gets or sets the PathGeometry that defines the path.
    /// </summary>
    public PathGeometry? PathGeometry { get; set; }

    /// <summary>
    /// Gets or sets whether to rotate the matrix to follow the path tangent.
    /// </summary>
    public bool DoesRotateWithTangent { get; set; }

    /// <summary>
    /// Gets or sets whether the animation is additive.
    /// </summary>
    public new bool IsAdditive { get; set; }

    /// <summary>
    /// Gets or sets whether the animation is cumulative across repeats.
    /// </summary>
    public new bool IsCumulative { get; set; }

    /// <summary>
    /// Gets or sets the offset angle when rotating with tangent.
    /// </summary>
    public double IsOffsetCumulative { get; set; }

    /// <inheritdoc />
    protected override Matrix GetCurrentValueCore(Matrix defaultOriginValue, Matrix defaultDestinationValue, AnimationClock animationClock)
    {
        var pathGeometry = PathGeometry;
        if (pathGeometry == null)
        {
            return defaultDestinationValue;
        }

        if (!_isValid)
        {
            Validate();
        }

        pathGeometry.GetPointAtFractionLength(animationClock.CurrentProgress, out var pathPoint, out var pathTangent);

        var offsetX = pathPoint.X;
        var offsetY = pathPoint.Y;
        var angle = 0.0;

        if (DoesRotateWithTangent)
        {
            angle = DoubleAnimationUsingPath.CalculateAngleFromTangentVector(pathTangent.X, pathTangent.Y);
        }

        // Handle cumulative behavior
        if (IsCumulative && animationClock.CurrentTime.HasValue)
        {
            var duration = Duration.HasTimeSpan ? Duration.TimeSpan.TotalMilliseconds : 1000;
            var currentRepeat = Math.Floor(animationClock.CurrentTime.Value.TotalMilliseconds / duration);
            if (currentRepeat > 0)
            {
                offsetX += _accumulatingOffset.X * currentRepeat;
                offsetY += _accumulatingOffset.Y * currentRepeat;
                if (DoesRotateWithTangent)
                {
                    angle += _accumulatingAngle * currentRepeat;
                }
            }
        }

        // Create the result matrix
        var result = Matrix.Identity;

        if (DoesRotateWithTangent)
        {
            var radians = angle * (Math.PI / 180.0);
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            result = new Matrix(cos, sin, -sin, cos, 0, 0);
        }

        result = new Matrix(result.M11, result.M12, result.M21, result.M22, offsetX, offsetY);

        if (IsAdditive)
        {
            return Matrix.Multiply(defaultOriginValue, result);
        }

        return result;
    }

    private void Validate()
    {
        if (IsCumulative && PathGeometry != null)
        {
            PathGeometry.GetPointAtFractionLength(0.0, out var startPoint, out var startTangent);
            PathGeometry.GetPointAtFractionLength(1.0, out var endPoint, out var endTangent);

            _accumulatingOffset = new Vector(
                endPoint.X - startPoint.X,
                endPoint.Y - startPoint.Y
            );

            if (DoesRotateWithTangent)
            {
                _accumulatingAngle = DoubleAnimationUsingPath.CalculateAngleFromTangentVector(endTangent.X, endTangent.Y) -
                                    DoubleAnimationUsingPath.CalculateAngleFromTangentVector(startTangent.X, startTangent.Y);
            }
        }

        _isValid = true;
    }
}
