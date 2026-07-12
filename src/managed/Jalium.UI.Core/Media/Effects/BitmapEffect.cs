using System.Runtime.InteropServices;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media.Effects;

/// <summary>
/// Legacy bitmap-effect base class. Jalium keeps the managed contract for WPF compatibility and
/// returns the supplied bitmap when a platform backend does not expose the retired unmanaged
/// WPF bitmap-effect pipeline.
/// </summary>
[Obsolete("BitmapEffect is deprecated. Use Effect-derived classes instead.")]
public abstract class BitmapEffect : Animatable
{
    /// <summary>Creates a modifiable clone of this bitmap effect.</summary>
    public new BitmapEffect Clone() => (BitmapEffect)base.Clone();

    /// <summary>Creates a modifiable clone using current property values.</summary>
    public new BitmapEffect CloneCurrentValue() => (BitmapEffect)base.CloneCurrentValue();

    /// <summary>
    /// Applies this effect to a concrete bitmap source. Backends without the deprecated native
    /// effect engine return the original source, while still exercising the derived effect's
    /// unmanaged-state hooks so custom compatibility implementations retain their lifecycle.
    /// </summary>
    public Imaging.BitmapSource GetOutput(BitmapEffectInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        Imaging.BitmapSource? source = input.Input;
        if (source is null)
        {
            throw new ArgumentException("There is no input set.", nameof(input));
        }

        if (ReferenceEquals(source, BitmapEffectInput.ContextInputSource))
        {
            throw new InvalidOperationException(
                "Cannot call BitmapEffect.GetOutput directly with a ContextInputSource. Provide a valid BitmapSource.");
        }

        using SafeHandle outer = CreateBitmapEffectOuter();
        using SafeHandle inner = CreateUnmanagedEffect();
        InitializeBitmapEffect(outer, inner);
        UpdateUnmanagedPropertyState(inner);
        return source;
    }

    /// <summary>Stores a compatibility property value on a managed effect handle.</summary>
    protected static void SetValue(SafeHandle effect, string propertyName, object value)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfInvalid(effect);

        if (effect is ManagedBitmapEffectHandle managed)
        {
            managed.Properties[propertyName] = value;
        }
    }

    /// <summary>Creates the outer compatibility handle used by legacy bitmap effects.</summary>
    protected static SafeHandle CreateBitmapEffectOuter() => new ManagedBitmapEffectHandle();

    /// <summary>Associates the outer compatibility object with its implementation handle.</summary>
    protected static void InitializeBitmapEffect(SafeHandle outerObject, SafeHandle innerObject)
    {
        ArgumentNullException.ThrowIfNull(outerObject);
        ArgumentNullException.ThrowIfNull(innerObject);
        ThrowIfInvalid(outerObject);
        ThrowIfInvalid(innerObject);

        if (outerObject is ManagedBitmapEffectHandle outer && innerObject is ManagedBitmapEffectHandle inner)
        {
            outer.InnerHandle = inner.DangerousGetHandle();
        }
    }

    /// <summary>Updates the state represented by an unmanaged compatibility handle.</summary>
    protected abstract void UpdateUnmanagedPropertyState(SafeHandle unmanagedEffect);

    /// <summary>Creates the implementation handle used by this legacy effect.</summary>
    protected abstract SafeHandle CreateUnmanagedEffect();

    /// <summary>Raises the Freezable change notification for dependency-property-backed effects.</summary>
    protected static void BitmapEffectPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BitmapEffect effect)
        {
            effect.WritePostscript();
        }
    }

    private static void ThrowIfInvalid(SafeHandle handle)
    {
        if (handle.IsClosed || handle.IsInvalid)
        {
            throw new ObjectDisposedException(nameof(handle));
        }
    }

    private sealed class ManagedBitmapEffectHandle : SafeHandle
    {
        private static long s_nextHandle;

        internal ManagedBitmapEffectHandle()
            : base(IntPtr.Zero, ownsHandle: true)
        {
            SetHandle((IntPtr)Interlocked.Increment(ref s_nextHandle));
        }

        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

        internal Dictionary<string, object> Properties { get; } = new(StringComparer.Ordinal);

        internal IntPtr InnerHandle { get; set; }

        protected override bool ReleaseHandle()
        {
            handle = IntPtr.Zero;
            Properties.Clear();
            InnerHandle = IntPtr.Zero;
            return true;
        }
    }
}

/// <summary>Describes the source and application rectangle for a legacy bitmap effect.</summary>
[Obsolete("BitmapEffect is deprecated.")]
public sealed class BitmapEffectInput : Animatable
{
    private static readonly Imaging.BitmapSource s_contextInputSource = new ContextBitmapSource();

    public static readonly DependencyProperty InputProperty =
        DependencyProperty.Register(
            nameof(Input),
            typeof(Imaging.BitmapSource),
            typeof(BitmapEffectInput),
            new PropertyMetadata(s_contextInputSource, OnInputChanged));

    public static readonly DependencyProperty AreaToApplyEffectUnitsProperty =
        DependencyProperty.Register(
            nameof(AreaToApplyEffectUnits),
            typeof(BrushMappingMode),
            typeof(BitmapEffectInput),
            new PropertyMetadata(BrushMappingMode.RelativeToBoundingBox, OnValueChanged),
            value => value is BrushMappingMode mode &&
                (mode == BrushMappingMode.Absolute || mode == BrushMappingMode.RelativeToBoundingBox));

    public static readonly DependencyProperty AreaToApplyEffectProperty =
        DependencyProperty.Register(
            nameof(AreaToApplyEffect),
            typeof(Rect),
            typeof(BitmapEffectInput),
            new PropertyMetadata(Rect.Empty, OnValueChanged));

    public BitmapEffectInput()
    {
    }

    public BitmapEffectInput(Imaging.BitmapSource input)
    {
        Input = input;
    }

    /// <summary>Gets the sentinel bitmap representing the element's current rendered content.</summary>
    public static Imaging.BitmapSource ContextInputSource => s_contextInputSource;

    public Imaging.BitmapSource? Input
    {
        get => (Imaging.BitmapSource?)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public BrushMappingMode AreaToApplyEffectUnits
    {
        get => (BrushMappingMode)(GetValue(AreaToApplyEffectUnitsProperty) ?? BrushMappingMode.RelativeToBoundingBox);
        set => SetValue(AreaToApplyEffectUnitsProperty, value);
    }

    public Rect AreaToApplyEffect
    {
        get => (Rect)(GetValue(AreaToApplyEffectProperty) ?? Rect.Empty);
        set => SetValue(AreaToApplyEffectProperty, value);
    }

    public bool ShouldSerializeInput() => !ReferenceEquals(Input, ContextInputSource);

    public new BitmapEffectInput Clone() => (BitmapEffectInput)base.Clone();

    public new BitmapEffectInput CloneCurrentValue() => (BitmapEffectInput)base.CloneCurrentValue();

#pragma warning disable CS0628 // WPF exposes the Freezable factory override on this sealed type.
    protected override Freezable CreateInstanceCore() => new BitmapEffectInput();
#pragma warning restore CS0628

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BitmapEffectInput input)
        {
            input.OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject);
            input.WritePostscript();
        }
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BitmapEffectInput input)
        {
            input.WritePostscript();
        }
    }

    private sealed class ContextBitmapSource : Imaging.BitmapSource
    {
        public override double Width => 0;

        public override double Height => 0;

        public override nint NativeHandle => 0;
    }
}
