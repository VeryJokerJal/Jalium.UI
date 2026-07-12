using System.Collections;
using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Markup;
using Jalium.UI.Media.Animation;
using AnimationHandoffBehavior = Jalium.UI.Media.Animation.HandoffBehavior;

namespace Jalium.UI;

/// <summary>
/// Provides framework-level services for nonvisual content, including resources,
/// styles, data context, logical parenting, names, initialization, and loaded state.
/// </summary>
public class FrameworkContentElement : ContentElement, IFrameworkInputElement, ISupportInitialize, IQueryAmbient
{
    public static readonly DependencyProperty BindingGroupProperty =
        FrameworkElement.BindingGroupProperty.AddOwner(
            typeof(FrameworkContentElement),
            new PropertyMetadata(null, null, null, inherits: true));

    public static readonly DependencyProperty ContextMenuProperty =
        FrameworkElement.ContextMenuProperty.AddOwner(typeof(FrameworkContentElement));

    public static readonly DependencyProperty CursorProperty =
        FrameworkElement.CursorProperty.AddOwner(
            typeof(FrameworkContentElement),
            new PropertyMetadata(null, null, null, inherits: true));

    public static readonly DependencyProperty DataContextProperty =
        FrameworkElement.DataContextProperty.AddOwner(
            typeof(FrameworkContentElement),
            new PropertyMetadata(null, OnDataContextPropertyChanged, null, inherits: true));

    protected internal static readonly DependencyProperty DefaultStyleKeyProperty =
        DependencyProperty.Register(
            nameof(DefaultStyleKey),
            typeof(object),
            typeof(FrameworkContentElement),
            new PropertyMetadata(null));

    public static readonly DependencyProperty FocusVisualStyleProperty =
        FrameworkElement.FocusVisualStyleProperty.AddOwner(typeof(FrameworkContentElement));

    public static readonly DependencyProperty ForceCursorProperty =
        FrameworkElement.ForceCursorProperty.AddOwner(typeof(FrameworkContentElement));

    public static readonly DependencyProperty InputScopeProperty =
        FrameworkElement.InputScopeProperty.AddOwner(
            typeof(FrameworkContentElement),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty LanguageProperty =
        FrameworkElement.LanguageProperty.AddOwner(
            typeof(FrameworkContentElement),
            new FrameworkPropertyMetadata(
                XmlLanguage.GetLanguage("en-US"),
                FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty NameProperty =
        FrameworkElement.NameProperty.AddOwner(typeof(FrameworkContentElement));

    public static readonly DependencyProperty OverridesDefaultStyleProperty =
        FrameworkElement.OverridesDefaultStyleProperty.AddOwner(typeof(FrameworkContentElement));

    public static readonly DependencyProperty StyleProperty =
        FrameworkElement.StyleProperty.AddOwner(
            typeof(FrameworkContentElement),
            new PropertyMetadata(null, OnStylePropertyChanged));

    public static readonly DependencyProperty TagProperty =
        FrameworkElement.TagProperty.AddOwner(typeof(FrameworkContentElement));

    public static readonly DependencyProperty ToolTipProperty =
        FrameworkElement.ToolTipProperty.AddOwner(typeof(FrameworkContentElement));

    public static readonly RoutedEvent LoadedEvent =
        FrameworkElement.LoadedEvent.AddOwner(typeof(FrameworkContentElement));

    public static readonly RoutedEvent UnloadedEvent =
        FrameworkElement.UnloadedEvent.AddOwner(typeof(FrameworkContentElement));

    public static readonly RoutedEvent ToolTipOpeningEvent =
        FrameworkElement.ToolTipOpeningEvent.AddOwner(typeof(FrameworkContentElement));

    public static readonly RoutedEvent ToolTipClosingEvent =
        FrameworkElement.ToolTipClosingEvent.AddOwner(typeof(FrameworkContentElement));

    public static readonly RoutedEvent ContextMenuOpeningEvent =
        FrameworkElement.ContextMenuOpeningEvent.AddOwner(typeof(FrameworkContentElement));

    public static readonly RoutedEvent ContextMenuClosingEvent =
        FrameworkElement.ContextMenuClosingEvent.AddOwner(typeof(FrameworkContentElement));

    private readonly List<object> _logicalChildren = [];
    private readonly Dictionary<string, object> _registeredNames = new(StringComparer.Ordinal);
    private readonly Dictionary<DependencyProperty, object> _dynamicResourceKeys = [];
    private DependencyObject? _logicalParent;
    private DependencyObject? _templatedParent;
    private ResourceDictionary? _resources;
    private int _initializationDepth;
    private bool _isInitialized;
    private bool _isLoaded;

    static FrameworkContentElement()
    {
        EventManager.RegisterClassHandler(
            typeof(FrameworkContentElement),
            ToolTipOpeningEvent,
            new ToolTipEventHandler(static (sender, e) =>
                ((FrameworkContentElement)sender).OnToolTipOpening(e)));
        EventManager.RegisterClassHandler(
            typeof(FrameworkContentElement),
            ToolTipClosingEvent,
            new ToolTipEventHandler(static (sender, e) =>
                ((FrameworkContentElement)sender).OnToolTipClosing(e)));
        EventManager.RegisterClassHandler(
            typeof(FrameworkContentElement),
            ContextMenuOpeningEvent,
            new ContextMenuEventHandler(static (sender, e) =>
                ((FrameworkContentElement)sender).OnContextMenuOpening(e)));
        EventManager.RegisterClassHandler(
            typeof(FrameworkContentElement),
            ContextMenuClosingEvent,
            new ContextMenuEventHandler(static (sender, e) =>
                ((FrameworkContentElement)sender).OnContextMenuClosing(e)));
    }

    public FrameworkContentElement()
    {
    }

    public BindingGroup? BindingGroup
    {
        get => (BindingGroup?)GetValue(BindingGroupProperty);
        set
        {
            BindingGroup? oldValue = BindingGroup;
            if (ReferenceEquals(oldValue, value))
            {
                return;
            }

            if (ReferenceEquals(oldValue?.Owner, this))
            {
                oldValue.SetOwner(null);
            }

            value?.SetOwner(this);
            SetValue(BindingGroupProperty, value);
        }
    }

    public ContextMenu? ContextMenu
    {
        get => (ContextMenu?)GetValue(ContextMenuProperty);
        set => SetValue(ContextMenuProperty, value);
    }

    public Cursor? Cursor
    {
        get => (Cursor?)GetValue(CursorProperty);
        set => SetValue(CursorProperty, value);
    }

    public object? DataContext
    {
        get
        {
            if (HasLocalValue(DataContextProperty))
            {
                return GetValue(DataContextProperty);
            }

            return Parent switch
            {
                FrameworkContentElement contentParent => contentParent.DataContext,
                FrameworkElement visualParent => visualParent.DataContext,
                _ => GetValue(DataContextProperty),
            };
        }
        set => SetValue(DataContextProperty, value);
    }

    protected internal object? DefaultStyleKey
    {
        get => GetValue(DefaultStyleKeyProperty);
        set => SetValue(DefaultStyleKeyProperty, value);
    }

    public Style? FocusVisualStyle
    {
        get => (Style?)GetValue(FocusVisualStyleProperty);
        set => SetValue(FocusVisualStyleProperty, value);
    }

    public bool ForceCursor
    {
        get => (bool)(GetValue(ForceCursorProperty) ?? false);
        set => SetValue(ForceCursorProperty, value);
    }

    public InputScope? InputScope
    {
        get => (InputScope?)GetValue(InputScopeProperty);
        set => SetValue(InputScopeProperty, value);
    }

    public bool IsInitialized => _isInitialized;

    public bool IsLoaded => _isLoaded;

    public XmlLanguage Language
    {
        get => (XmlLanguage)(GetValue(LanguageProperty) ?? XmlLanguage.GetLanguage("en-US"));
        set => SetValue(LanguageProperty, value ?? throw new ArgumentNullException(nameof(value)));
    }

    protected internal virtual IEnumerator LogicalChildren => _logicalChildren.GetEnumerator();

    public string Name
    {
        get => (string)(GetValue(NameProperty) ?? string.Empty);
        set => SetValue(NameProperty, value);
    }

    public bool OverridesDefaultStyle
    {
        get => (bool)(GetValue(OverridesDefaultStyleProperty) ?? false);
        set => SetValue(OverridesDefaultStyleProperty, value);
    }

    public DependencyObject? Parent => _logicalParent ?? base.GetUIParentCore();

    public ResourceDictionary Resources
    {
        get
        {
            if (_resources is null)
            {
                _resources = new ResourceDictionary();
                _resources.Changed += OnResourcesChanged;
                Diagnostics.ResourceDictionaryDiagnosticsStore.RegisterOwner(
                    _resources,
                    this,
                    Diagnostics.ResourceDictionaryOwnerKind.FrameworkContentElement);
            }

            return _resources;
        }
        set
        {
            if (ReferenceEquals(_resources, value))
            {
                return;
            }

            if (_resources is not null)
            {
                _resources.Changed -= OnResourcesChanged;
                Diagnostics.ResourceDictionaryDiagnosticsStore.UnregisterOwner(_resources, this);
            }

            _resources = value ?? new ResourceDictionary();
            _resources.Changed += OnResourcesChanged;
            Diagnostics.ResourceDictionaryDiagnosticsStore.RegisterOwner(
                _resources,
                this,
                Diagnostics.ResourceDictionaryOwnerKind.FrameworkContentElement);
            OnResourcesChanged(_resources, EventArgs.Empty);
        }
    }

    public Style? Style
    {
        get => (Style?)GetValue(StyleProperty);
        set => SetValue(StyleProperty, value);
    }

    public object? Tag
    {
        get => GetValue(TagProperty);
        set => SetValue(TagProperty, value);
    }

    public DependencyObject? TemplatedParent => _templatedParent;

    public object? ToolTip
    {
        get => GetValue(ToolTipProperty);
        set => SetValue(ToolTipProperty, value);
    }

    public event ContextMenuEventHandler ContextMenuClosing
    {
        add => AddHandler(ContextMenuClosingEvent, value);
        remove => RemoveHandler(ContextMenuClosingEvent, value);
    }

    public event ContextMenuEventHandler ContextMenuOpening
    {
        add => AddHandler(ContextMenuOpeningEvent, value);
        remove => RemoveHandler(ContextMenuOpeningEvent, value);
    }

    public event DependencyPropertyChangedEventHandler? DataContextChanged;

    public event EventHandler? Initialized;

    public event RoutedEventHandler Loaded
    {
        add => AddHandler(LoadedEvent, value);
        remove => RemoveHandler(LoadedEvent, value);
    }

    public event EventHandler<DataTransferEventArgs> SourceUpdated
    {
        add => AddHandler(Binding.SourceUpdatedEvent, value);
        remove => RemoveHandler(Binding.SourceUpdatedEvent, value);
    }

    public event EventHandler<DataTransferEventArgs> TargetUpdated
    {
        add => AddHandler(Binding.TargetUpdatedEvent, value);
        remove => RemoveHandler(Binding.TargetUpdatedEvent, value);
    }

    public event ToolTipEventHandler ToolTipClosing
    {
        add => AddHandler(ToolTipClosingEvent, value);
        remove => RemoveHandler(ToolTipClosingEvent, value);
    }

    public event ToolTipEventHandler ToolTipOpening
    {
        add => AddHandler(ToolTipOpeningEvent, value);
        remove => RemoveHandler(ToolTipOpeningEvent, value);
    }

    public event RoutedEventHandler Unloaded
    {
        add => AddHandler(UnloadedEvent, value);
        remove => RemoveHandler(UnloadedEvent, value);
    }

    protected internal void AddLogicalChild(object? child)
    {
        if (child is null)
        {
            return;
        }

        if (ReferenceEquals(child, this))
        {
            throw new InvalidOperationException("An element cannot be its own logical child.");
        }

        if (_logicalChildren.Contains(child))
        {
            return;
        }

        if (child is FrameworkContentElement contentChild)
        {
            if (contentChild.Parent is not null && !ReferenceEquals(contentChild.Parent, this))
            {
                throw new InvalidOperationException("The logical child already has a parent.");
            }

            contentChild._logicalParent = this;
            contentChild.SetContentParent(this);
            contentChild.ReactivateBindings();
            if (IsLoaded)
            {
                contentChild.SetLoadedState(true);
            }
        }
        else if (child is ContentElement contentElement)
        {
            if (contentElement.GetUIParentCore() is not null &&
                !ReferenceEquals(contentElement.GetUIParentCore(), this))
            {
                throw new InvalidOperationException("The logical child already has a parent.");
            }

            contentElement.SetContentParent(this);
        }

        _logicalChildren.Add(child);
        ResourceLookup.InvalidateResourceCache();
    }

    public virtual void BeginInit()
    {
        if (_isInitialized)
        {
            throw new InvalidOperationException("An initialized FrameworkContentElement cannot begin initialization again.");
        }

        _initializationDepth++;
    }

    public void BeginStoryboard(Storyboard storyboard) =>
        BeginStoryboard(storyboard, AnimationHandoffBehavior.SnapshotAndReplace, isControllable: false);

    public void BeginStoryboard(Storyboard storyboard, AnimationHandoffBehavior handoffBehavior) =>
        BeginStoryboard(storyboard, handoffBehavior, isControllable: false);

    public void BeginStoryboard(
        Storyboard storyboard,
        AnimationHandoffBehavior handoffBehavior,
        bool isControllable)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        if (!Enum.IsDefined(handoffBehavior))
        {
            throw new InvalidEnumArgumentException(
                nameof(handoffBehavior),
                (int)handoffBehavior,
                typeof(AnimationHandoffBehavior));
        }

        // Storyboard is hosted in Core and therefore cannot depend on this Controls-layer
        // content type. Its parameterless path still applies explicitly targeted timelines.
        storyboard.Begin();
    }

    public void BringIntoView()
    {
        RaiseEvent(new RequestBringIntoViewEventArgs(FrameworkElement.RequestBringIntoViewEvent, this)
        {
            TargetObject = this,
            TargetRect = Rect.Empty,
        });
    }

    public virtual void EndInit()
    {
        if (_isInitialized)
        {
            return;
        }

        if (_initializationDepth > 0)
        {
            _initializationDepth--;
        }

        if (_initializationDepth == 0)
        {
            EnsureInitialized();
        }
    }

    public object? FindName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (NameScope.GetNameScope(this)?.FindName(name) is { } scoped)
        {
            return scoped;
        }

        if (_registeredNames.TryGetValue(name, out object? local))
        {
            return local;
        }

        return Parent switch
        {
            FrameworkContentElement contentParent => contentParent.FindName(name),
            FrameworkElement visualParent => visualParent.FindName(name),
            _ => null,
        };
    }

    public object FindResource(object resourceKey)
    {
        ArgumentNullException.ThrowIfNull(resourceKey);
        return TryFindResource(resourceKey)
            ?? throw new InvalidOperationException($"Resource '{resourceKey}' not found.");
    }

    public new BindingExpression? GetBindingExpression(DependencyProperty dp) =>
        base.GetBindingExpression(dp) as BindingExpression;

    protected internal override DependencyObject? GetUIParentCore() => Parent;

    public sealed override bool MoveFocus(TraversalRequest request) => base.MoveFocus(request);

    protected virtual void OnContextMenuClosing(ContextMenuEventArgs e)
    {
    }

    protected virtual void OnContextMenuOpening(ContextMenuEventArgs e)
    {
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);
    }

    protected virtual void OnInitialized(EventArgs e) => Initialized?.Invoke(this, e);

    protected internal virtual void OnStyleChanged(Style? oldStyle, Style? newStyle)
    {
    }

    protected virtual void OnToolTipClosing(ToolTipEventArgs e)
    {
    }

    protected virtual void OnToolTipOpening(ToolTipEventArgs e)
    {
    }

    public sealed override DependencyObject? PredictFocus(FocusNavigationDirection direction)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new InvalidEnumArgumentException(
                nameof(direction),
                (int)direction,
                typeof(FocusNavigationDirection));
        }

        return base.PredictFocus(direction);
    }

    public void RegisterName(string name, object scopedElement)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(scopedElement);

        if (NameScope.GetNameScope(this) is { } nameScope)
        {
            nameScope.RegisterName(name, scopedElement);
            return;
        }

        if (!_registeredNames.TryAdd(name, scopedElement))
        {
            throw new ArgumentException($"Name '{name}' is already registered in this scope.", nameof(name));
        }
    }

    protected internal void RemoveLogicalChild(object? child)
    {
        if (child is null || !_logicalChildren.Remove(child))
        {
            return;
        }

        if (child is FrameworkContentElement contentChild && ReferenceEquals(contentChild.Parent, this))
        {
            if (contentChild.IsLoaded)
            {
                contentChild.SetLoadedState(false);
            }

            contentChild._logicalParent = null;
            contentChild.SetContentParent(null);
        }
        else if (child is ContentElement contentElement &&
                 ReferenceEquals(contentElement.GetUIParentCore(), this))
        {
            contentElement.SetContentParent(null);
        }

        ResourceLookup.InvalidateResourceCache();
    }

    public BindingExpression SetBinding(DependencyProperty dp, string path)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ArgumentNullException.ThrowIfNull(path);
        return (BindingExpression)SetBinding(dp, new Binding(path));
    }

    public void SetResourceReference(DependencyProperty dp, object name)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ArgumentNullException.ThrowIfNull(name);
        _dynamicResourceKeys[dp] = name;
        RefreshDynamicResource(dp, name);
    }

    public bool ShouldSerializeResources() => _resources is { Count: > 0 };

    public bool ShouldSerializeStyle() => HasLocalValue(StyleProperty);

    public object? TryFindResource(object resourceKey)
    {
        ArgumentNullException.ThrowIfNull(resourceKey);

        for (FrameworkContentElement? current = this;
             current is not null;
             current = current.Parent as FrameworkContentElement)
        {
            if (current._resources?.TryGetValue(resourceKey, out object? local) == true)
            {
                return local;
            }

            if (current.Style?.Resources.TryGetValue(resourceKey, out object? styled) == true)
            {
                return styled;
            }

            if (current.Parent is FrameworkElement visualParent)
            {
                return ResourceLookup.FindResource(visualParent, resourceKey);
            }
        }

        return ResourceLookup.ApplicationResourceLookup?.Invoke(resourceKey);
    }

    public void UnregisterName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (NameScope.GetNameScope(this) is { } nameScope)
        {
            nameScope.UnregisterName(name);
            return;
        }

        if (!_registeredNames.Remove(name))
        {
            throw new ArgumentException($"Name '{name}' was not found in this scope.", nameof(name));
        }
    }

    public void UpdateDefaultStyle()
    {
        if (OverridesDefaultStyle || HasLocalValue(StyleProperty))
        {
            return;
        }

        object key = DefaultStyleKey ?? GetType();
        if (TryFindResource(key) is Style style)
        {
            Style = style;
        }
    }

    internal void CopyLogicalContentChildrenTo(Stack<ContentElement> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        foreach (ContentElement child in _logicalChildren.OfType<ContentElement>())
        {
            destination.Push(child);
        }
    }

    internal void SetLoadedState(bool loaded)
    {
        if (_isLoaded == loaded)
        {
            return;
        }

        if (loaded)
        {
            EnsureInitialized();
        }

        _isLoaded = loaded;
        RaiseEvent(new RoutedEventArgs(loaded ? LoadedEvent : UnloadedEvent, this));

        foreach (FrameworkContentElement child in _logicalChildren.OfType<FrameworkContentElement>())
        {
            child.SetLoadedState(loaded);
        }
    }

    internal void SetTemplatedParent(DependencyObject? templatedParent)
    {
        _templatedParent = templatedParent;
        ReactivateBindings();
    }

    internal override void OnContentParentChanged(DependencyObject? oldParent, DependencyObject? newParent)
    {
        base.OnContentParentChanged(oldParent, newParent);
        _logicalParent = newParent;
        ReactivateBindings();
        RefreshDynamicResources();

        bool loaded = newParent switch
        {
            FrameworkContentElement contentParent => contentParent.IsLoaded,
            FrameworkElement visualParent => visualParent.IsLoaded,
            _ => false,
        };
        SetLoadedState(loaded);
    }

    bool IQueryAmbient.IsAmbientPropertyAvailable(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        return propertyName switch
        {
            nameof(Resources) => _resources is not null,
            nameof(Style) => HasLocalValue(StyleProperty),
            _ => DependencyProperty.FromName(GetType(), propertyName) is { } property && HasLocalValue(property),
        };
    }

    private void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        OnInitialized(EventArgs.Empty);
    }

    private void OnResourcesChanged(object? sender, EventArgs e)
    {
        ResourceLookup.InvalidateResourceCache();
        RefreshDynamicResources();

        foreach (FrameworkContentElement child in _logicalChildren.OfType<FrameworkContentElement>())
        {
            child.OnResourcesChanged(sender, e);
        }
    }

    private void RefreshDynamicResources()
    {
        foreach ((DependencyProperty property, object resourceKey) in _dynamicResourceKeys.ToArray())
        {
            RefreshDynamicResource(property, resourceKey);
        }
    }

    private void RefreshDynamicResource(DependencyProperty property, object resourceKey)
    {
        object? value = TryFindResource(resourceKey);
        if (value is null)
        {
            ClearValue(property);
        }
        else
        {
            SetValue(property, value);
        }
    }

    private static void OnDataContextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkContentElement element)
        {
            return;
        }

        element.ReactivateBindings();
        element.DataContextChanged?.Invoke(element, e);
        foreach (FrameworkContentElement child in element._logicalChildren.OfType<FrameworkContentElement>())
        {
            if (!child.HasLocalValue(DataContextProperty))
            {
                child.ReactivateBindings();
                child.DataContextChanged?.Invoke(child, e);
            }
        }
    }

    private static void OnStylePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkContentElement element)
        {
            element.OnStyleChanged(e.OldValue as Style, e.NewValue as Style);
            ResourceLookup.InvalidateResourceCache();
        }
    }
}
