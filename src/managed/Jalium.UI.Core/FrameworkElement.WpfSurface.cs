using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using AnimationHandoffBehavior = Jalium.UI.Media.Animation.HandoffBehavior;

namespace Jalium.UI;

public partial class FrameworkElement
{
    /// <summary>Identifies the <see cref="InputScope"/> dependency property.</summary>
    public static readonly DependencyProperty InputScopeProperty =
        DependencyProperty.Register(
            nameof(InputScope),
            typeof(InputScope),
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Identifies the <see cref="Language"/> dependency property.</summary>
    public static readonly DependencyProperty LanguageProperty =
        DependencyProperty.RegisterAttached(
            nameof(Language),
            typeof(XmlLanguage),
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(
                XmlLanguage.GetLanguage("en-US"),
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Identifies the <see cref="ToolTipOpening"/> routed event.</summary>
    public static readonly RoutedEvent ToolTipOpeningEvent =
        EventManager.RegisterRoutedEvent(
            nameof(ToolTipOpening),
            RoutingStrategy.Direct,
            typeof(ToolTipEventHandler),
            typeof(FrameworkElement));

    /// <summary>Identifies the <see cref="ToolTipClosing"/> routed event.</summary>
    public static readonly RoutedEvent ToolTipClosingEvent =
        EventManager.RegisterRoutedEvent(
            nameof(ToolTipClosing),
            RoutingStrategy.Direct,
            typeof(ToolTipEventHandler),
            typeof(FrameworkElement));

    /// <summary>Identifies the <see cref="ContextMenuOpening"/> routed event.</summary>
    public static readonly RoutedEvent ContextMenuOpeningEvent =
        EventManager.RegisterRoutedEvent(
            nameof(ContextMenuOpening),
            RoutingStrategy.Bubble,
            typeof(ContextMenuEventHandler),
            typeof(FrameworkElement));

    /// <summary>Identifies the <see cref="ContextMenuClosing"/> routed event.</summary>
    public static readonly RoutedEvent ContextMenuClosingEvent =
        EventManager.RegisterRoutedEvent(
            nameof(ContextMenuClosing),
            RoutingStrategy.Bubble,
            typeof(ContextMenuEventHandler),
            typeof(FrameworkElement));

    private TriggerCollection? _triggers;

    static FrameworkElement()
    {
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            ToolTipOpeningEvent,
            new ToolTipEventHandler(static (sender, e) => ((FrameworkElement)sender).OnToolTipOpening(e)));
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            ToolTipClosingEvent,
            new ToolTipEventHandler(static (sender, e) => ((FrameworkElement)sender).OnToolTipClosing(e)));
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            ContextMenuOpeningEvent,
            new ContextMenuEventHandler(static (sender, e) => ((FrameworkElement)sender).OnContextMenuOpening(e)));
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            ContextMenuClosingEvent,
            new ContextMenuEventHandler(static (sender, e) => ((FrameworkElement)sender).OnContextMenuClosing(e)));
    }

    /// <summary>Gets or sets the context for alternative text-input methods.</summary>
    public InputScope? InputScope
    {
        get => (InputScope?)GetValue(InputScopeProperty);
        set => SetValue(InputScopeProperty, value);
    }

    /// <summary>Gets or sets the localization language inherited by this element.</summary>
    public XmlLanguage Language
    {
        get => (XmlLanguage)(GetValue(LanguageProperty) ?? XmlLanguage.GetLanguage("en-US"));
        set => SetValue(LanguageProperty, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Gets triggers established directly on this element.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public TriggerCollection Triggers => _triggers ??= new TriggerCollection(this);

    public event ToolTipEventHandler ToolTipOpening
    {
        add => AddHandler(ToolTipOpeningEvent, value);
        remove => RemoveHandler(ToolTipOpeningEvent, value);
    }

    public event ToolTipEventHandler ToolTipClosing
    {
        add => AddHandler(ToolTipClosingEvent, value);
        remove => RemoveHandler(ToolTipClosingEvent, value);
    }

    public event ContextMenuEventHandler ContextMenuOpening
    {
        add => AddHandler(ContextMenuOpeningEvent, value);
        remove => RemoveHandler(ContextMenuOpeningEvent, value);
    }

    public event ContextMenuEventHandler ContextMenuClosing
    {
        add => AddHandler(ContextMenuClosingEvent, value);
        remove => RemoveHandler(ContextMenuClosingEvent, value);
    }

    public event EventHandler<DataTransferEventArgs> TargetUpdated
    {
        add => AddHandler(Binding.TargetUpdatedEvent, value);
        remove => RemoveHandler(Binding.TargetUpdatedEvent, value);
    }

    public event EventHandler<DataTransferEventArgs> SourceUpdated
    {
        add => AddHandler(Binding.SourceUpdatedEvent, value);
        remove => RemoveHandler(Binding.SourceUpdatedEvent, value);
    }

    /// <summary>Returns the ordinary binding expression on <paramref name="dp"/>.</summary>
    public new BindingExpression? GetBindingExpression(DependencyProperty dp)
        => base.GetBindingExpression(dp) as BindingExpression;

    /// <summary>Creates a binding from a source property path.</summary>
    public BindingExpression SetBinding(DependencyProperty dp, string path)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ArgumentNullException.ThrowIfNull(path);
        return (BindingExpression)SetBinding(dp, new Binding(path));
    }

    /// <summary>Begins a storyboard using snapshot-and-replace handoff.</summary>
    public void BeginStoryboard(Storyboard storyboard)
        => BeginStoryboard(storyboard, AnimationHandoffBehavior.SnapshotAndReplace, isControllable: false);

    /// <summary>Begins a storyboard with the specified handoff behavior.</summary>
    public void BeginStoryboard(Storyboard storyboard, AnimationHandoffBehavior handoffBehavior)
        => BeginStoryboard(storyboard, handoffBehavior, isControllable: false);

    /// <summary>Begins a storyboard and optionally exposes its control operations.</summary>
    public void BeginStoryboard(
        Storyboard storyboard,
        AnimationHandoffBehavior handoffBehavior,
        bool isControllable)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        storyboard.Begin(this, handoffBehavior, isControllable);
    }

    /// <summary>Returns whether direct element triggers should be serialized.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeTriggers() => _triggers is { Count: > 0 };

    /// <summary>Determines the next element for directional focus without moving focus.</summary>
    public sealed override DependencyObject? PredictFocus(FocusNavigationDirection direction)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new InvalidEnumArgumentException(nameof(direction), (int)direction, typeof(FocusNavigationDirection));
        }

        return base.PredictFocus(direction);
    }

    /// <summary>Invoked when an unhandled tooltip-opening event reaches this class.</summary>
    protected virtual void OnToolTipOpening(ToolTipEventArgs e)
    {
    }

    /// <summary>Invoked when an unhandled tooltip-closing event reaches this class.</summary>
    protected virtual void OnToolTipClosing(ToolTipEventArgs e)
    {
    }

    /// <summary>Invoked when an unhandled context-menu-opening event reaches this class.</summary>
    protected virtual void OnContextMenuOpening(ContextMenuEventArgs e)
    {
    }

    /// <summary>Invoked when an unhandled context-menu-closing event reaches this class.</summary>
    protected virtual void OnContextMenuClosing(ContextMenuEventArgs e)
    {
    }

    /// <summary>Raises size notifications after the render size changes.</summary>
    protected internal override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        ArgumentNullException.ThrowIfNull(sizeInfo);
        _previousRenderSize = sizeInfo.NewSize;
        SetValue(ActualWidthPropertyKey, sizeInfo.NewSize.Width);
        SetValue(ActualHeightPropertyKey, sizeInfo.NewSize.Height);
        OnSizeChanged(sizeInfo);
    }

    /// <summary>Invoked when this element's active style changes.</summary>
    protected internal virtual void OnStyleChanged(Style? oldStyle, Style? newStyle)
    {
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    /// <summary>Notifies a specialized layout parent that a child invalidated parent layout.</summary>
    protected internal virtual void ParentLayoutInvalidated(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
    }

    /// <inheritdoc />
    protected override Geometry? GetLayoutClip(Size layoutSlotSize)
        => base.GetLayoutClip(layoutSlotSize);
}
