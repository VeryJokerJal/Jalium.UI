using System.ComponentModel;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Ink;

/// <summary>
/// Specifies the appearance of a <see cref="Stroke"/> when rendered.
/// </summary>
public sealed class DrawingAttributes : INotifyPropertyChanged
{
    /// <summary>
    /// The default width value.
    /// </summary>
    public const double DefaultWidth = 2.0;

    /// <summary>
    /// The default height value.
    /// </summary>
    public const double DefaultHeight = 2.0;

    private Color _color = Color.FromRgb(0, 0, 0);
    private double _width = DefaultWidth;
    private double _height = DefaultHeight;
    private StylusTip _stylusTip = StylusTip.Ellipse;
    private bool _isHighlighter;
    private bool _fitToCurve = true;
    private bool _ignorePressure;
    private BrushType _brushType = BrushType.Round;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Occurs when any attribute changes.
    /// </summary>
    public event EventHandler? AttributeChanged;

    /// <summary>
    /// Gets or sets the color of the stroke.
    /// </summary>
    public Color Color
    {
        get => _color;
        set
        {
            if (_color != value)
            {
                _color = value;
                OnPropertyChanged(nameof(Color));
            }
        }
    }

    /// <summary>
    /// Gets or sets the width of the stroke.
    /// </summary>
    public double Width
    {
        get => _width;
        set
        {
            if (!_width.Equals(value))
            {
                _width = Math.Max(0, value);
                OnPropertyChanged(nameof(Width));
            }
        }
    }

    /// <summary>
    /// Gets or sets the height of the stroke.
    /// </summary>
    public double Height
    {
        get => _height;
        set
        {
            if (!_height.Equals(value))
            {
                _height = Math.Max(0, value);
                OnPropertyChanged(nameof(Height));
            }
        }
    }

    /// <summary>
    /// Gets or sets the shape of the stylus tip.
    /// </summary>
    public StylusTip StylusTip
    {
        get => _stylusTip;
        set
        {
            if (_stylusTip != value)
            {
                _stylusTip = value;
                OnPropertyChanged(nameof(StylusTip));
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether this stroke uses highlighter rendering.
    /// </summary>
    /// <remarks>
    /// When true, the stroke is rendered with lower opacity to simulate a highlighter pen.
    /// </remarks>
    public bool IsHighlighter
    {
        get => _isHighlighter;
        set
        {
            if (_isHighlighter != value)
            {
                _isHighlighter = value;
                OnPropertyChanged(nameof(IsHighlighter));
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether Bezier curve fitting is applied to smooth the stroke.
    /// </summary>
    public bool FitToCurve
    {
        get => _fitToCurve;
        set
        {
            if (_fitToCurve != value)
            {
                _fitToCurve = value;
                OnPropertyChanged(nameof(FitToCurve));
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether pressure information is ignored during rendering.
    /// </summary>
    public bool IgnorePressure
    {
        get => _ignorePressure;
        set
        {
            if (_ignorePressure != value)
            {
                _ignorePressure = value;
                OnPropertyChanged(nameof(IgnorePressure));
            }
        }
    }

    /// <summary>
    /// Gets or sets the brush type for stroke rendering.
    /// </summary>
    public BrushType BrushType
    {
        get => _brushType;
        set
        {
            if (_brushType != value)
            {
                _brushType = value;
                OnPropertyChanged(nameof(BrushType));
            }
        }
    }

    /// <summary>
    /// Creates a copy of this <see cref="DrawingAttributes"/>.
    /// </summary>
    /// <returns>A new <see cref="DrawingAttributes"/> with the same values.</returns>
    public DrawingAttributes Clone()
    {
        return new DrawingAttributes
        {
            Color = Color,
            Width = Width,
            Height = Height,
            StylusTip = StylusTip,
            IsHighlighter = IsHighlighter,
            FitToCurve = FitToCurve,
            IgnorePressure = IgnorePressure,
            BrushType = BrushType
        };
    }

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event.
    /// </summary>
    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        AttributeChanged?.Invoke(this, EventArgs.Empty);
    }
}
