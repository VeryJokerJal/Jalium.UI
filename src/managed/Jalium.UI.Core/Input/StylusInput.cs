using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Media;

namespace Jalium.UI.Input;

/// <summary>Provides state and packet queries for a stylus device.</summary>
public sealed partial class StylusDevice : InputDevice
{
    private IInputElement? _captured;
    private IInputElement? _rawDirectlyOver;
    private IInputElement? _directlyOver;
    private CaptureMode _captureMode;
    private PresentationSource? _activeSource;
    private readonly StylusButton _barrelButton = new("Barrel", StylusPointProperties.BarrelButton.Id);
    private readonly StylusButton _eraserButton = new("Eraser", Guid.Parse("2F77EA8B-7F39-4FC2-9D0A-36A930AFB85E"));
    private readonly StylusButtonCollection _stylusButtons;
    private StylusPointCollection _points = new();
    private Point _position;

    internal StylusDevice(int id, string? name = null)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? $"PointerStylus{id}" : name;
        _barrelButton.StylusDevice = this;
        _eraserButton.StylusDevice = this;
        _stylusButtons = new StylusButtonCollection([_barrelButton, _eraserButton]);
    }

    public override IInputElement? Target => DirectlyOver;
    public override PresentationSource? ActiveSource => _activeSource;

    public int Id { get; }
    public string Name { get; }
    public StylusButtonCollection StylusButtons => _stylusButtons;
    public TabletDevice? TabletDevice => null;

    public IInputElement? DirectlyOver
    {
        get => _directlyOver;
        internal set
        {
            _rawDirectlyOver = value;
            _directlyOver = ResolveDirectlyOver(value);
        }
    }
    public IInputElement? Captured => _captured;
    public CaptureMode CaptureMode => _captureMode;
    public bool InAir { get; internal set; }
    public bool Inverted { get; internal set; }
    public bool InRange { get; internal set; }
    public bool IsValid { get; internal set; } = true;

    public bool Capture(IInputElement? element) => Capture(element, CaptureMode.Element);

    public bool Capture(IInputElement? element, CaptureMode captureMode)
    {
        if (!Enum.IsDefined(captureMode))
            throw new InvalidEnumArgumentException(nameof(captureMode), (int)captureMode, typeof(CaptureMode));
        if (element is null || captureMode == CaptureMode.None)
        {
            element = null;
            captureMode = CaptureMode.None;
        }
        else if (!element.IsEnabled)
        {
            return false;
        }

        if (ReferenceEquals(_captured, element) && _captureMode == captureMode)
            return true;

        _captured = element;
        _captureMode = captureMode;
        _directlyOver = ResolveDirectlyOver(_rawDirectlyOver);
        UIElement.SetStylusCapturedElement(element as UIElement);
        Synchronize();
        return true;
    }

    public Point GetPosition(IInputElement? relativeTo) =>
        InputCoordinateHelper.FromRoot(_position, relativeTo);

    public StylusPointCollection GetStylusPoints(IInputElement? relativeTo)
    {
        StylusPointCollection result = _points.Clone();
        for (int index = 0; index < result.Count; index++)
        {
            StylusPoint point = result[index];
            Point transformed = InputCoordinateHelper.FromRoot(point.ToPoint(), relativeTo);
            point.X = transformed.X;
            point.Y = transformed.Y;
            result[index] = point;
        }

        return result;
    }

    public StylusPointCollection GetStylusPoints(
        IInputElement? relativeTo,
        StylusPointDescription subsetToReformatTo)
    {
        ArgumentNullException.ThrowIfNull(subsetToReformatTo);
        return GetStylusPoints(relativeTo).Reformat(subsetToReformatTo);
    }

    public void Synchronize()
    {
        UIElement.SetStylusDirectlyOverElement(DirectlyOver as UIElement);
        UIElement.SetStylusCapturedElement(Captured as UIElement);
    }

    internal void SetActiveSource(PresentationSource? activeSource) => _activeSource = activeSource;

    internal void UpdateState(
        Point position,
        StylusPointCollection stylusPoints,
        bool inAir,
        bool inverted,
        bool inRange,
        bool barrelPressed,
        bool eraserPressed,
        UIElement? directlyOver)
    {
        ArgumentNullException.ThrowIfNull(stylusPoints);
        _position = position;
        InAir = inAir;
        Inverted = inverted;
        InRange = inRange;
        DirectlyOver = directlyOver;
        _barrelButton.StylusButtonState = barrelPressed ? StylusButtonState.Down : StylusButtonState.Up;
        _eraserButton.StylusButtonState = eraserPressed ? StylusButtonState.Down : StylusButtonState.Up;
        _points = stylusPoints.Count > 0
            ? new StylusPointCollection(stylusPoints)
            : new StylusPointCollection(stylusPoints.Description);
        Synchronize();
    }

    internal void UpdateState(
        Point position,
        float pressureFactor,
        bool inAir,
        bool inverted,
        bool inRange,
        bool barrelPressed,
        bool eraserPressed,
        UIElement? directlyOver) =>
        UpdateState(
            position,
            new StylusPointCollection([new StylusPoint(position.X, position.Y, pressureFactor)]),
            inAir,
            inverted,
            inRange,
            barrelPressed,
            eraserPressed,
            directlyOver);

    internal void Invalidate() => IsValid = false;

    public override string ToString() =>
        string.Format(CultureInfo.CurrentCulture, "{0}({1})", base.ToString(), Name);

    private IInputElement? ResolveDirectlyOver(IInputElement? rawDirectlyOver)
    {
        if (_captured is null || _captureMode == CaptureMode.None)
            return rawDirectlyOver;
        if (_captureMode == CaptureMode.Element)
            return _captured;
        if (ReferenceEquals(rawDirectlyOver, _captured))
            return rawDirectlyOver;
        if (rawDirectlyOver is not Visual visual || _captured is not Visual capturedVisual)
            return _captured;
        Visual? current = visual.VisualParent;
        while (current is not null)
        {
            if (ReferenceEquals(current, capturedVisual))
                return rawDirectlyOver;
            current = current.VisualParent;
        }
        return _captured;
    }
}

/// <summary>
/// Compatibility adapter for code that previously constructed Jalium's mutable pointer-specific
/// stylus subclass. WPF exposes <see cref="StylusDevice"/> as sealed, so the adapter now owns the
/// sealed device and converts to it at input API boundaries.
/// </summary>
public sealed class PointerStylusDevice
{
    public PointerStylusDevice(int id, string? name = null)
    {
        Device = new StylusDevice(id, name);
    }

    public StylusDevice Device { get; }
    public int Id => Device.Id;
    public string Name => Device.Name;
    public StylusButtonCollection StylusButtons => Device.StylusButtons;
    public TabletDevice? TabletDevice => Device.TabletDevice;
    public IInputElement? Target => Device.Target;
    public PresentationSource? ActiveSource => Device.ActiveSource;
    public IInputElement? DirectlyOver => Device.DirectlyOver;
    public IInputElement? Captured => Device.Captured;
    public CaptureMode CaptureMode => Device.CaptureMode;
    public bool InAir => Device.InAir;
    public bool Inverted => Device.Inverted;
    public bool InRange => Device.InRange;
    public bool IsValid => Device.IsValid;

    public bool Capture(IInputElement? element) => Device.Capture(element);

    public bool Capture(IInputElement? element, CaptureMode captureMode) =>
        Device.Capture(element, captureMode);

    public void UpdateState(
        Point position,
        StylusPointCollection stylusPoints,
        bool inAir,
        bool inverted,
        bool inRange,
        bool barrelPressed,
        bool eraserPressed,
        UIElement? directlyOver)
    {
        Device.UpdateState(
            position,
            stylusPoints,
            inAir,
            inverted,
            inRange,
            barrelPressed,
            eraserPressed,
            directlyOver);
    }

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
        Device.UpdateState(
            position,
            pressureFactor,
            inAir,
            inverted,
            inRange,
            barrelPressed,
            eraserPressed,
            directlyOver);
    }

    public Point GetPosition(IInputElement? relativeTo) => Device.GetPosition(relativeTo);

    public StylusPointCollection GetStylusPoints(IInputElement? relativeTo) =>
        Device.GetStylusPoints(relativeTo);

    public StylusPointCollection GetStylusPoints(
        IInputElement? relativeTo,
        StylusPointDescription subsetToReformatTo) =>
        Device.GetStylusPoints(relativeTo, subsetToReformatTo);

    public void Synchronize() => Device.Synchronize();

    public static implicit operator StylusDevice(PointerStylusDevice pointerDevice)
    {
        ArgumentNullException.ThrowIfNull(pointerDevice);
        return pointerDevice.Device;
    }

    public override string ToString() => Device.ToString();
}

/// <summary>Represents a digitizer tablet.</summary>
public sealed partial class TabletDevice : InputDevice
{
    private readonly ReadOnlyCollection<StylusPointProperty> _supportedStylusPointProperties;
    private readonly StylusDeviceCollection _stylusDevices;

    internal TabletDevice(
        int id = 0,
        string? name = null,
        string? productId = null,
        TabletDeviceType type = TabletDeviceType.Stylus,
        TabletHardwareCapabilities capabilities = TabletHardwareCapabilities.None,
        IList<StylusPointProperty>? supportedStylusPointProperties = null,
        IList<StylusDevice>? stylusDevices = null)
    {
        Id = id;
        Name = name ?? string.Empty;
        ProductId = productId ?? string.Empty;
        Type = type;
        TabletHardwareCapabilities = capabilities;
        _supportedStylusPointProperties = new ReadOnlyCollection<StylusPointProperty>(
            supportedStylusPointProperties ?? new List<StylusPointProperty>());
        _stylusDevices = new StylusDeviceCollection(
            stylusDevices ?? new List<StylusDevice>());
    }

    public override IInputElement? Target => null;
    public override PresentationSource? ActiveSource => null;

    public int Id { get; }
    public string Name { get; }
    public string ProductId { get; }
    public TabletDeviceType Type { get; }
    public TabletHardwareCapabilities TabletHardwareCapabilities { get; }
    public ReadOnlyCollection<StylusPointProperty> SupportedStylusPointProperties =>
        _supportedStylusPointProperties;
    public StylusDeviceCollection StylusDevices => _stylusDevices;

    public override string ToString() => Name;
}

/// <summary>Provides static access to the process's tablet and stylus state.</summary>
public static class Tablet
{
    private static StylusDevice? _currentStylusDevice;

    static Tablet()
    {
        UIElement.StylusCaptureRequested = static element =>
            CurrentStylusDevice?.Capture(element) == true;
        UIElement.StylusCaptureReleaseRequested = static element =>
        {
            if (ReferenceEquals(CurrentStylusDevice?.Captured, element))
                CurrentStylusDevice.Capture(null);
            else if (ReferenceEquals(UIElement.StylusCapturedElement, element))
                UIElement.SetStylusCapturedElement(null);
        };
    }

    public static TabletDeviceCollection TabletDevices { get; } = new();

    /// <summary>Gets the tablet associated with the current stylus, when available.</summary>
    public static TabletDevice? CurrentTabletDevice =>
        CurrentStylusDevice?.TabletDevice ??
        (TabletDevices.Count == 0 ? null : TabletDevices[0]);

    public static StylusDevice? CurrentStylusDevice
    {
        get => _currentStylusDevice;
        set
        {
            _currentStylusDevice = value;
            UIElement.SetStylusDirectlyOverElement(value?.DirectlyOver as UIElement);
            UIElement.SetStylusCapturedElement(value?.Captured as UIElement);
        }
    }
}

/// <summary>Read-only collection of active tablet devices.</summary>
public partial class TabletDeviceCollection : ICollection, IEnumerable
{
    private readonly List<TabletDevice> _items;

    public TabletDeviceCollection()
        : this(new List<TabletDevice>())
    {
    }

    internal TabletDeviceCollection(IList<TabletDevice> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        _items = new List<TabletDevice>(list);
    }

    public TabletDevice this[int index] => _items[index];
    public int Count => _items.Count;

    public bool IsSynchronized => false;
    public object SyncRoot => ((ICollection)_items).SyncRoot;
    public void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public IEnumerator GetEnumerator() => _items.GetEnumerator();

    internal void Add(TabletDevice tabletDevice)
    {
        ArgumentNullException.ThrowIfNull(tabletDevice);
        if (!_items.Contains(tabletDevice))
            _items.Add(tabletDevice);
    }

    internal bool Remove(TabletDevice tabletDevice) => _items.Remove(tabletDevice);
}

/// <summary>Read-only collection of stylus devices exposed by a tablet.</summary>
public class StylusDeviceCollection : ReadOnlyCollection<StylusDevice>
{
    public StylusDeviceCollection() : base(new List<StylusDevice>()) { }
    internal StylusDeviceCollection(IList<StylusDevice> list) : base(list) { }
}

/// <summary>Represents a physical button on a stylus.</summary>
public sealed class StylusButton
{
    public StylusButton(string name, Guid guid)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Guid = guid;
    }

    public string Name { get; }
    public Guid Guid { get; }
    public StylusButtonState StylusButtonState { get; internal set; }
    public StylusDevice? StylusDevice { get; internal set; }
    public override string ToString() => Name;
}

/// <summary>Read-only collection of a stylus device's buttons.</summary>
public sealed class StylusButtonCollection : ReadOnlyCollection<StylusButton>
{
    public StylusButtonCollection() : base(new List<StylusButton>()) { }
    internal StylusButtonCollection(IList<StylusButton> list) : base(list) { }

    public StylusButton? GetStylusButtonByGuid(Guid guid)
        => this.FirstOrDefault(button => button.Guid == guid);
}

/// <summary>Describes the properties carried by each packet in a point collection.</summary>
public class StylusPointDescription
{
    internal const int RequiredPropertyCount = 3;
    private const int MaximumButtonCount = 31;
    private readonly StylusPointPropertyInfo[] _properties;
    private readonly int _buttonCount;

    public StylusPointDescription()
        : this([
            StylusPointPropertyInfo.CreateDefault(StylusPointProperties.X),
            StylusPointPropertyInfo.CreateDefault(StylusPointProperties.Y),
            StylusPointPropertyInfo.CreateDefault(StylusPointProperties.NormalPressure)])
    {
    }

    public StylusPointDescription(IEnumerable<StylusPointPropertyInfo> stylusPointPropertyInfos)
    {
        ArgumentNullException.ThrowIfNull(stylusPointPropertyInfos);
        _properties = stylusPointPropertyInfos.ToArray();
        if (_properties.Length < RequiredPropertyCount ||
            _properties[0].Id != StylusPointProperties.X.Id ||
            _properties[1].Id != StylusPointProperties.Y.Id ||
            _properties[2].Id != StylusPointProperties.NormalPressure.Id)
        {
            throw new ArgumentException("A description must begin with X, Y, and NormalPressure.", nameof(stylusPointPropertyInfos));
        }

        HashSet<Guid> seen = [];
        int buttonCount = 0;
        foreach (StylusPointPropertyInfo property in _properties)
        {
            ArgumentNullException.ThrowIfNull(property);
            if (!seen.Add(property.Id))
                throw new ArgumentException("A stylus point description cannot contain duplicate properties.", nameof(stylusPointPropertyInfos));
            if (property.IsButton)
            {
                buttonCount++;
            }
        }
        if (buttonCount > MaximumButtonCount)
            throw new ArgumentException("A description cannot contain more than 31 buttons.", nameof(stylusPointPropertyInfos));
        _buttonCount = buttonCount;
    }

    public int PropertyCount => _properties.Length;
    internal int OutputValueCount => RequiredPropertyCount + (_properties.Length - RequiredPropertyCount - _buttonCount) + (_buttonCount > 0 ? 1 : 0);

    public ReadOnlyCollection<StylusPointPropertyInfo> GetStylusPointProperties()
        => new(_properties);

    public bool HasProperty(StylusPointProperty stylusPointProperty)
    {
        ArgumentNullException.ThrowIfNull(stylusPointProperty);
        return GetPropertyIndex(stylusPointProperty.Id) >= 0;
    }

    public StylusPointPropertyInfo GetPropertyInfo(StylusPointProperty stylusPointProperty)
    {
        ArgumentNullException.ThrowIfNull(stylusPointProperty);
        int index = GetPropertyIndex(stylusPointProperty.Id);
        if (index < 0)
            throw new ArgumentException("The description does not contain that property.", nameof(stylusPointProperty));
        return _properties[index];
    }

    public static bool AreCompatible(StylusPointDescription first, StylusPointDescription second)
    {
        if (first is null || second is null)
            throw new ArgumentNullException("stylusPointDescription");
        if (first._properties.Length != second._properties.Length)
            return false;
        for (int index = RequiredPropertyCount; index < first._properties.Length; index++)
        {
            if (first._properties[index].Id != second._properties[index].Id ||
                first._properties[index].IsButton != second._properties[index].IsButton)
            {
                return false;
            }
        }
        return true;
    }

    public static StylusPointDescription GetCommonDescription(
        StylusPointDescription stylusPointDescription,
        StylusPointDescription stylusPointDescriptionPreserveInfo)
    {
        ArgumentNullException.ThrowIfNull(stylusPointDescription);
        ArgumentNullException.ThrowIfNull(stylusPointDescriptionPreserveInfo);

        List<StylusPointPropertyInfo> common =
        [
            stylusPointDescriptionPreserveInfo._properties[0],
            stylusPointDescriptionPreserveInfo._properties[1],
            stylusPointDescriptionPreserveInfo._properties[2],
        ];
        for (int sourceIndex = RequiredPropertyCount; sourceIndex < stylusPointDescription._properties.Length; sourceIndex++)
        {
            StylusPointPropertyInfo source = stylusPointDescription._properties[sourceIndex];
            for (int preserveIndex = RequiredPropertyCount; preserveIndex < stylusPointDescriptionPreserveInfo._properties.Length; preserveIndex++)
            {
                StylusPointPropertyInfo preserve = stylusPointDescriptionPreserveInfo._properties[preserveIndex];
                if (source.Id == preserve.Id && source.IsButton == preserve.IsButton)
                {
                    common.Add(preserve);
                    break;
                }
            }
        }
        return new StylusPointDescription(common);
    }

    public bool IsSubsetOf(StylusPointDescription stylusPointDescriptionSuperset)
    {
        ArgumentNullException.ThrowIfNull(stylusPointDescriptionSuperset);
        if (stylusPointDescriptionSuperset.PropertyCount < PropertyCount)
            return false;
        return _properties.All(property => stylusPointDescriptionSuperset.GetPropertyIndex(property.Id) >= 0);
    }

    internal int GetPropertyIndex(Guid id)
    {
        for (int index = 0; index < _properties.Length; index++)
        {
            if (_properties[index].Id == id)
                return index;
        }
        return -1;
    }

    internal int GetButtonBitPosition(StylusPointProperty property)
    {
        if (!property.IsButton)
            throw new InvalidOperationException("The property is not a button.");
        int bitPosition = 0;
        for (int index = RequiredPropertyCount; index < _properties.Length; index++)
        {
            if (!_properties[index].IsButton)
                continue;
            if (_properties[index].Id == property.Id)
                return bitPosition;
            bitPosition++;
        }
        throw new ArgumentException("The button is not in this description.", nameof(property));
    }

    internal int[] CreateAdditionalDataBuffer()
        => new int[OutputValueCount - RequiredPropertyCount];
}

/// <summary>Identifies one value in a stylus packet.</summary>
public class StylusPointProperty
{
    public StylusPointProperty(Guid identifier, bool isButton)
    {
        Id = identifier;
        IsButton = isButton;
    }

    protected StylusPointProperty(StylusPointProperty stylusPointProperty)
    {
        ArgumentNullException.ThrowIfNull(stylusPointProperty);
        Id = stylusPointProperty.Id;
        IsButton = stylusPointProperty.IsButton;
    }

    public Guid Id { get; }
    public bool IsButton { get; }

    public override string ToString()
    {
        string name = StylusPointPropertyIds.TryGet(Id, out string? knownName, out _) ? knownName! : "Unknown";
        return $"{{Id={name}, IsButton={IsButton.ToString(CultureInfo.InvariantCulture)}}}";
    }
}

/// <summary>Adds physical units and range metadata to a stylus packet property.</summary>
public class StylusPointPropertyInfo : StylusPointProperty
{
    public StylusPointPropertyInfo(StylusPointProperty stylusPointProperty)
        : this(CreateDefault(stylusPointProperty), copy: true)
    {
    }

    private StylusPointPropertyInfo(StylusPointPropertyInfo source, bool copy)
        : base(source)
    {
        Minimum = source.Minimum;
        Maximum = source.Maximum;
        Unit = source.Unit;
        Resolution = source.Resolution;
    }

    public StylusPointPropertyInfo(
        StylusPointProperty stylusPointProperty,
        int minimum,
        int maximum,
        StylusPointPropertyUnit unit,
        float resolution)
        : base(stylusPointProperty)
    {
        if (!Enum.IsDefined(unit))
            throw new InvalidEnumArgumentException(nameof(unit), (int)unit, typeof(StylusPointPropertyUnit));
        if (maximum < minimum)
            throw new ArgumentException("Maximum cannot be less than minimum.", nameof(maximum));
        if (resolution < 0f)
            throw new ArgumentException("Resolution cannot be negative.", nameof(resolution));
        Minimum = minimum;
        Maximum = maximum;
        Unit = unit;
        Resolution = resolution;
    }

    public int Minimum { get; }
    public int Maximum { get; }
    public StylusPointPropertyUnit Unit { get; }
    public float Resolution { get; }

    internal static StylusPointPropertyInfo CreateDefault(StylusPointProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (property.Id == StylusPointProperties.X.Id || property.Id == StylusPointProperties.Y.Id)
            return new StylusPointPropertyInfo(property, int.MinValue, int.MaxValue, StylusPointPropertyUnit.Centimeters, 1000f);
        if (property.Id == StylusPointProperties.NormalPressure.Id)
            return new StylusPointPropertyInfo(property, 0, 1023, StylusPointPropertyUnit.None, 1f);
        if (property.IsButton)
            return new StylusPointPropertyInfo(property, 0, 1, StylusPointPropertyUnit.None, 1f);
        return new StylusPointPropertyInfo(property, int.MinValue, int.MaxValue, StylusPointPropertyUnit.None, 1f);
    }
}

/// <summary>Well-known stylus packet properties.</summary>
public static class StylusPointProperties
{
    public static readonly StylusPointProperty X = Create("598A6A8F-52C0-4BA0-93AF-AF357411A561", false);
    public static readonly StylusPointProperty Y = Create("B53F9F75-04E0-4498-A7EE-C30DBB5A9011", false);
    public static readonly StylusPointProperty Z = Create("735ADB30-0EBB-4788-A0E4-0F316490055D", false);
    public static readonly StylusPointProperty Width = Create("BAABE94D-2712-48F5-BE9D-8F8B5EA0711A", false);
    public static readonly StylusPointProperty Height = Create("E61858D2-E447-4218-9D3F-18865C203DF4", false);
    public static readonly StylusPointProperty SystemTouch = Create("E706C804-57F0-4F00-8A0C-853D57789BE9", false);
    public static readonly StylusPointProperty PacketStatus = Create("6E0E07BF-AFE7-4CF7-87D1-AF6446208418", false);
    public static readonly StylusPointProperty SerialNumber = Create("78A81B56-0935-4493-BAAE-00541A8A16C4", false);
    public static readonly StylusPointProperty NormalPressure = Create("7307502D-F9F4-4E18-B3F2-2CE1B1A3610C", false);
    public static readonly StylusPointProperty TangentPressure = Create("6DA4488B-5244-41EC-905B-32D89AB80809", false);
    public static readonly StylusPointProperty ButtonPressure = Create("8B7FEFC4-96AA-4BFE-AC26-8A5F0BE07BF5", false);
    public static readonly StylusPointProperty XTiltOrientation = Create("A8D07B3A-8BF0-40B0-95A9-B80A6BB787BF", false);
    public static readonly StylusPointProperty YTiltOrientation = Create("0E932389-1D77-43AF-AC00-5B950D6D4B2D", false);
    public static readonly StylusPointProperty AzimuthOrientation = Create("029123B4-8828-410B-B250-A0536595E5DC", false);
    public static readonly StylusPointProperty AltitudeOrientation = Create("82DEC5C7-F6BA-4906-894F-66D68DFC456C", false);
    public static readonly StylusPointProperty TwistOrientation = Create("0D324960-13B2-41E4-ACE6-7AE9D43D2D3B", false);
    public static readonly StylusPointProperty PitchRotation = Create("7F7E57B7-BE37-4BE1-A356-7A84160E1893", false);
    public static readonly StylusPointProperty RollRotation = Create("5D5D5E56-6BA9-4C5B-9FB0-851C91714E56", false);
    public static readonly StylusPointProperty YawRotation = Create("6A849980-7C3A-45B7-AA82-90A262950E89", false);
    public static readonly StylusPointProperty TipButton = Create("039143D3-78CB-449C-A8E7-67D18864C332", true);
    public static readonly StylusPointProperty BarrelButton = Create("F0720328-663B-418F-85A6-9531AE3ECDFA", true);
    public static readonly StylusPointProperty SecondaryTipButton = Create("67743782-0EE5-419A-A12B-273A9EC08F3D", true);

    private static StylusPointProperty Create(string id, bool isButton) => new(Guid.Parse(id), isButton);
}

internal static class StylusPointPropertyIds
{
    private static readonly Dictionary<Guid, (string Name, bool IsButton)> Known = new()
    {
        [Guid.Parse("598A6A8F-52C0-4BA0-93AF-AF357411A561")] = ("X", false),
        [Guid.Parse("B53F9F75-04E0-4498-A7EE-C30DBB5A9011")] = ("Y", false),
        [Guid.Parse("735ADB30-0EBB-4788-A0E4-0F316490055D")] = ("Z", false),
        [Guid.Parse("BAABE94D-2712-48F5-BE9D-8F8B5EA0711A")] = ("Width", false),
        [Guid.Parse("E61858D2-E447-4218-9D3F-18865C203DF4")] = ("Height", false),
        [Guid.Parse("E706C804-57F0-4F00-8A0C-853D57789BE9")] = ("SystemTouch", false),
        [Guid.Parse("6E0E07BF-AFE7-4CF7-87D1-AF6446208418")] = ("PacketStatus", false),
        [Guid.Parse("78A81B56-0935-4493-BAAE-00541A8A16C4")] = ("SerialNumber", false),
        [Guid.Parse("7307502D-F9F4-4E18-B3F2-2CE1B1A3610C")] = ("NormalPressure", false),
        [Guid.Parse("6DA4488B-5244-41EC-905B-32D89AB80809")] = ("TangentPressure", false),
        [Guid.Parse("8B7FEFC4-96AA-4BFE-AC26-8A5F0BE07BF5")] = ("ButtonPressure", false),
        [Guid.Parse("A8D07B3A-8BF0-40B0-95A9-B80A6BB787BF")] = ("XTiltOrientation", false),
        [Guid.Parse("0E932389-1D77-43AF-AC00-5B950D6D4B2D")] = ("YTiltOrientation", false),
        [Guid.Parse("029123B4-8828-410B-B250-A0536595E5DC")] = ("AzimuthOrientation", false),
        [Guid.Parse("82DEC5C7-F6BA-4906-894F-66D68DFC456C")] = ("AltitudeOrientation", false),
        [Guid.Parse("0D324960-13B2-41E4-ACE6-7AE9D43D2D3B")] = ("TwistOrientation", false),
        [Guid.Parse("7F7E57B7-BE37-4BE1-A356-7A84160E1893")] = ("PitchRotation", false),
        [Guid.Parse("5D5D5E56-6BA9-4C5B-9FB0-851C91714E56")] = ("RollRotation", false),
        [Guid.Parse("6A849980-7C3A-45B7-AA82-90A262950E89")] = ("YawRotation", false),
        [Guid.Parse("039143D3-78CB-449C-A8E7-67D18864C332")] = ("TipButton", true),
        [Guid.Parse("F0720328-663B-418F-85A6-9531AE3ECDFA")] = ("BarrelButton", true),
        [Guid.Parse("67743782-0EE5-419A-A12B-273A9EC08F3D")] = ("SecondaryTipButton", true),
    };

    internal static bool TryGet(Guid id, out string? name, out bool isButton)
    {
        if (Known.TryGetValue(id, out (string Name, bool IsButton) value))
        {
            name = value.Name;
            isButton = value.IsButton;
            return true;
        }
        name = null;
        isButton = false;
        return false;
    }
}

internal static class InputCoordinateHelper
{
    internal static Point FromRoot(Point source, IInputElement? relativeTo)
    {
        if (relativeTo is not UIElement element)
            return source;

        List<Visual> chain = [];
        Visual? current = element;
        while (current is not null)
        {
            chain.Add(current);
            current = current.VisualParent;
        }

        Point point = source;
        for (int index = chain.Count - 2; index >= 0; index--)
        {
            Visual child = chain[index];
            if (child is FrameworkElement frameworkElement)
                point = new Point(point.X - frameworkElement.VisualBounds.X, point.Y - frameworkElement.VisualBounds.Y);

            if (child is UIElement uiElement && uiElement.RenderTransform is { } transform && transform.Value.TryInvert(out Matrix inverse))
            {
                Point origin = uiElement.RenderTransformOrigin;
                Size size = uiElement.RenderSize;
                double originX = origin.X * size.Width;
                double originY = origin.Y * size.Height;
                Point translated = new(point.X - originX, point.Y - originY);
                Point inverted = inverse.Transform(translated);
                point = new Point(inverted.X + originX, inverted.Y + originY);
            }
        }
        return point;
    }
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
