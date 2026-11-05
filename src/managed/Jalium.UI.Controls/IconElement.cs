using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;
/// <summary>
/// Represents the base class for an icon UI element.
/// Mirrors WinUI's Microsoft.UI.Xaml.Controls.IconElement.
/// </summary>
public abstract class IconElement : Control
{
    private static readonly SolidColorBrush s_defaultForegroundBrush = new(ThemeColors.TextPrimary);
    private List<ForegroundObserver>? _foregroundAncestors;
    protected IconElement()
    {
        Loaded += (_, _) => ObserveForegroundAncestors();
        Unloaded += (_, _) => StopObservingForegroundAncestors();
    }

    protected internal override void OnVisualParentChanged(DependencyObject? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        ObserveForegroundAncestors();
        InvalidateIconContent();
    }

    private void ObserveForegroundAncestors()
    {
        StopObservingForegroundAncestors();
        for (Visual? parent = ParentVisual; parent is not null; parent = parent.VisualParent)
            (_foregroundAncestors ??= []).Add(new ForegroundObserver(this, parent));
    }

    private void StopObservingForegroundAncestors()
    {
        if (_foregroundAncestors is null)
            return;
        foreach (var observer in _foregroundAncestors)
            observer.Detach();
        _foregroundAncestors.Clear();
    }

    private sealed class ForegroundObserver
    {
        private readonly WeakReference<IconElement> _icon;
        private readonly WeakReference<DependencyObject> _source;
        internal ForegroundObserver(IconElement icon, DependencyObject source)
        {
            _icon = new(icon);
            _source = new(source);
            source.PropertyChangedInternal += OnPropertyChanged;
        }

        internal void Detach()
        {
            if (_source.TryGetTarget(out var source))
                source.PropertyChangedInternal -= OnPropertyChanged;
        }

        private void OnPropertyChanged(DependencyProperty property, object? oldValue, object? newValue)
        {
            if (!_icon.TryGetTarget(out var icon))
            {
                Detach();
                return;
            }

            // Inherited values resolve lazily. Brush replacement must also retire
            // drawings in the template. Weak links cannot retain retired icons.
            if (property == ForegroundProperty)
                icon.InvalidateIconContent();
        }
    }

    private void InvalidateIconContent() => InvalidateIconSubtree(this);
    private static void InvalidateIconSubtree(Visual visual)
    {
        if (visual is UIElement element)
            element.InvalidateVisual();
        else
            visual.SetRenderDirty();
        for (int i = 0; i < visual.VisualChildrenCount; i++)
            if (visual.GetVisualChild(i)is { } child)
                InvalidateIconSubtree(child);
    }

    /// <summary>
    /// Identifies the Foreground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public new static readonly DependencyProperty ForegroundProperty = Control.ForegroundProperty.AddOwner(typeof(IconElement), new PropertyMetadata(null, OnForegroundChanged, null, inherits: true));
    /// <summary>
    /// Gets or sets the foreground brush for the icon.
    /// When null, inherits from the parent visual tree.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public new Brush? Foreground { get => (Brush? )GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>
    /// Resolves the effective foreground brush by walking up the visual tree.
    /// </summary>
    protected Brush GetEffectiveForeground()
    {
        if (Foreground != null)
            return Foreground;
        // Walk up to find Foreground from a parent Control
        Visual? current = ParentVisual;
        while (current != null)
        {
            if (current is Control control && control.Foreground != null)
                return control.Foreground;
            current = current.VisualParent;
        }

        return TryFindResource("TextPrimary") as Brush ?? s_defaultForegroundBrush;
    }

    private static void OnForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is IconElement icon)
            icon.InvalidateIconContent();
    }
}
