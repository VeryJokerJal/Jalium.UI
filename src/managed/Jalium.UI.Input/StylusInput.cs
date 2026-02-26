using System.Collections.ObjectModel;
using Jalium.UI;

namespace Jalium.UI.Input;

/// <summary>
/// Provides access to general information about a tablet pen/stylus device.
/// </summary>
public abstract class StylusDevice : InputDevice
{
    public override UIElement? Target => DirectlyOver;
    public override object? ActiveSource => null;

    public abstract int Id { get; }
    public abstract string Name { get; }
    public abstract StylusButtonCollection StylusButtons { get; }
    public abstract TabletDevice? TabletDevice { get; }

    public UIElement? DirectlyOver { get; internal set; }
    public UIElement? Captured { get; internal set; }
    public bool InAir { get; internal set; }
    public bool Inverted { get; internal set; }
    public bool InRange { get; internal set; }

    public bool Capture(UIElement? element)
    {
        Captured = element;
        return true;
    }

    public virtual Point GetPosition(UIElement? relativeTo) => default;
    public virtual InputStylusPointCollection GetStylusPoints(UIElement? relativeTo) => new();
}

/// <summary>
/// Mutable stylus device used by window-level pointer dispatch.
/// </summary>
public sealed class PointerStylusDevice : StylusDevice
{
    private readonly StylusButton _barrelButton = new("Barrel", Guid.Parse("F0720328-663B-418F-85A6-9531AE3ECDFA"));
    private readonly StylusButton _eraserButton = new("Eraser", Guid.Parse("2F77EA8B-7F39-4FC2-9D0A-36A930AFB85E"));
    private readonly StylusButtonCollection _stylusButtons;
    private InputStylusPointCollection _points = new();
    private Point _position;

    public PointerStylusDevice(int id, string? name = null)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? $"PointerStylus{id}" : name;
        _barrelButton.StylusDevice = this;
        _eraserButton.StylusDevice = this;
        _stylusButtons = new StylusButtonCollection(new List<StylusButton> { _barrelButton, _eraserButton });
    }

    public override int Id { get; }
    public override string Name { get; }
    public override StylusButtonCollection StylusButtons => _stylusButtons;
    public override TabletDevice? TabletDevice => null;

    public void UpdateState(
        Point position,
        float pressureFactor,
        bool inAir,
        bool inverted,
        bool inRange,
        bool barrelPressed,
        bool eraserPressed,
        UIElement? directlyOver)
    {
        _position = position;
        InAir = inAir;
        Inverted = inverted;
        InRange = inRange;
        DirectlyOver = directlyOver;
        _barrelButton.StylusButtonState = barrelPressed ? StylusButtonState.Down : StylusButtonState.Up;
        _eraserButton.StylusButtonState = eraserPressed ? StylusButtonState.Down : StylusButtonState.Up;
        _points = new InputStylusPointCollection(new[] { new InputStylusPoint(position.X, position.Y, pressureFactor) });
    }

    public override Point GetPosition(UIElement? relativeTo) => _position;

    public override InputStylusPointCollection GetStylusPoints(UIElement? relativeTo)
    {
        return new InputStylusPointCollection(_points);
    }
}

/// <summary>
/// Represents a tablet digitizer device.
/// </summary>
public abstract class TabletDevice : InputDevice
{
    public override UIElement? Target => null;
    public override object? ActiveSource => null;

    public abstract int Id { get; }
    public abstract string Name { get; }
    public abstract TabletDeviceType Type { get; }
    public abstract TabletHardwareCapabilities TabletHardwareCapabilities { get; }
    public abstract StylusPointDescription SupportedStylusPointProperties { get; }
    public abstract ReadOnlyCollection<StylusDevice> StylusDevices { get; }
}

/// <summary>
/// Provides static access to tablet/stylus devices.
/// </summary>
public static class Tablet
{
    public static TabletDeviceCollection TabletDevices { get; } = new();
    public static StylusDevice? CurrentStylusDevice { get; internal set; }
}

/// <summary>
/// Collection of tablet devices.
/// </summary>
public sealed class TabletDeviceCollection : ReadOnlyCollection<TabletDevice>
{
    public TabletDeviceCollection() : base(new List<TabletDevice>()) { }
    internal TabletDeviceCollection(IList<TabletDevice> list) : base(list) { }
}

/// <summary>
/// Represents a button on a stylus.
/// </summary>
public sealed class StylusButton
{
    public StylusButton(string name, Guid guid)
    {
        Name = name;
        Guid = guid;
    }
    public string Name { get; }
    public Guid Guid { get; }
    public StylusButtonState StylusButtonState { get; internal set; }
    public StylusDevice? StylusDevice { get; internal set; }
}

/// <summary>
/// Collection of stylus buttons.
/// </summary>
public sealed class StylusButtonCollection : ReadOnlyCollection<StylusButton>
{
    public StylusButtonCollection() : base(new List<StylusButton>()) { }
    internal StylusButtonCollection(IList<StylusButton> list) : base(list) { }
}

/// <summary>
/// Describes the properties available from a stylus point.
/// </summary>
public sealed class StylusPointDescription
{
    private readonly ReadOnlyCollection<StylusPointPropertyInfo> _properties;

    public StylusPointDescription() : this(new List<StylusPointPropertyInfo>
    {
        new(StylusPointProperties.X),
        new(StylusPointProperties.Y),
        new(StylusPointProperties.NormalPressure)
    }) { }

    public StylusPointDescription(IEnumerable<StylusPointPropertyInfo> stylusPointPropertyInfos)
    {
        _properties = new ReadOnlyCollection<StylusPointPropertyInfo>(new List<StylusPointPropertyInfo>(stylusPointPropertyInfos));
    }

    public int PropertyCount => _properties.Count;
    public ReadOnlyCollection<StylusPointPropertyInfo> GetStylusPointProperties() => _properties;
    public bool HasProperty(StylusPointProperty stylusPointProperty) => _properties.Any(p => p.Id == stylusPointProperty.Id);

    public static bool AreCompatible(StylusPointDescription a, StylusPointDescription b) => true;
}

/// <summary>
/// Represents a stylus point property.
/// </summary>
public class StylusPointProperty
{
    public StylusPointProperty(Guid identifier, bool isButton)
    {
        Id = identifier;
        IsButton = isButton;
    }
    public Guid Id { get; }
    public bool IsButton { get; }
}

/// <summary>
/// Extended information about a stylus point property.
/// </summary>
public sealed class StylusPointPropertyInfo : StylusPointProperty
{
    public StylusPointPropertyInfo(StylusPointProperty stylusPointProperty)
        : base(stylusPointProperty.Id, stylusPointProperty.IsButton)
    {
        Minimum = 0;
        Maximum = stylusPointProperty.IsButton ? 1 : int.MaxValue;
        Resolution = 1.0f;
        Unit = StylusPointPropertyUnit.None;
    }

    public StylusPointPropertyInfo(StylusPointProperty stylusPointProperty, int minimum, int maximum, StylusPointPropertyUnit unit, float resolution)
        : base(stylusPointProperty.Id, stylusPointProperty.IsButton)
    {
        Minimum = minimum;
        Maximum = maximum;
        Unit = unit;
        Resolution = resolution;
    }

    public int Minimum { get; }
    public int Maximum { get; }
    public StylusPointPropertyUnit Unit { get; }
    public float Resolution { get; }
}

/// <summary>
/// Standard stylus point properties.
/// </summary>
public static class StylusPointProperties
{
    public static readonly StylusPointProperty X = new(new Guid("598A6A8F-52C0-4BA0-93AF-AF357411A561"), false);
    public static readonly StylusPointProperty Y = new(new Guid("B53F9F75-04E0-4498-A7EE-C30DBB5A9011"), false);
    public static readonly StylusPointProperty Z = new(new Guid("735ADB30-0EBB-4788-A0E4-0F316490055D"), false);
    public static readonly StylusPointProperty NormalPressure = new(new Guid("7307502D-F109-44F0-BEB9-3F3ABFDCE52D"), false);
    public static readonly StylusPointProperty TangentPressure = new(new Guid("6DA4488B-5244-41EC-905B-32D89AB80809"), false);
    public static readonly StylusPointProperty ButtonPressure = new(new Guid("8B7FEFC4-96AA-4BFE-AC26-8A5F0BE07BF5"), false);
    public static readonly StylusPointProperty XTiltOrientation = new(new Guid("A8D07B3A-8BF8-40B0-95A9-B80A6BB787BF"), false);
    public static readonly StylusPointProperty YTiltOrientation = new(new Guid("0E932389-1D77-43AF-AC00-5B950D6D4B2D"), false);
    public static readonly StylusPointProperty AzimuthOrientation = new(new Guid("029123B4-8828-410B-B250-A0536595E5DC"), false);
    public static readonly StylusPointProperty AltitudeOrientation = new(new Guid("82DEC5C7-F6BA-4906-894F-66D68DFC456C"), false);
    public static readonly StylusPointProperty TwistOrientation = new(new Guid("0D324960-13B2-41E4-ACE6-7AE9D43D2D3B"), false);
    public static readonly StylusPointProperty Width = new(new Guid("BAABE94D-2712-48F5-BE9D-8F8B5EA0711A"), false);
    public static readonly StylusPointProperty Height = new(new Guid("E61858D2-E447-4218-9D3F-18865C203DF4"), false);
    public static readonly StylusPointProperty SystemTouch = new(new Guid("E706C804-57F0-4F00-8A0C-853D57789BE9"), false);
    public static readonly StylusPointProperty TipButton = new(new Guid("39143D3E-78CB-449C-A8E7-67D188816992"), true);
    public static readonly StylusPointProperty BarrelButton = new(new Guid("F0720328-663B-418F-85A6-9531AE3ECDFA"), true);
}

/// <summary>
/// Represents a single stylus input point in the input layer.
/// </summary>
public struct InputStylusPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public float PressureFactor { get; set; }

    public InputStylusPoint(double x, double y) : this(x, y, 0.5f) { }

    public InputStylusPoint(double x, double y, float pressureFactor)
    {
        X = x;
        Y = y;
        PressureFactor = pressureFactor;
    }
}

/// <summary>
/// Collection of stylus points for the input layer.
/// </summary>
public sealed class InputStylusPointCollection : Collection<InputStylusPoint>
{
    public InputStylusPointCollection() { }
    public InputStylusPointCollection(IEnumerable<InputStylusPoint> points) : base(new List<InputStylusPoint>(points)) { }
}

public enum StylusButtonState { Up, Down }
public enum TabletDeviceType { Stylus, Touch }

[Flags]
public enum TabletHardwareCapabilities
{
    None = 0,
    Integrated = 0x1,
    StylusMustTouch = 0x2,
    HardProximity = 0x4,
    StylusHasPhysicalIds = 0x8,
    SupportsPressure = 0x40000000
}

public enum StylusPointPropertyUnit
{
    None,
    Inches,
    Centimeters,
    Degrees,
    Radians,
    Seconds,
    Pounds,
    Grams
}
