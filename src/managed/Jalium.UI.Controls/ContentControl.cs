using System.Globalization;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Controls;

/// <summary>
/// Base class for content controls.
/// Uses direct content management by default. Controls with ControlTemplate (like Button)
/// rely on the template's ContentPresenter to display content instead.
/// </summary>
public partial class ContentControl : Control, IAddChild
{
    private UIElement? _contentElement;
    private bool _usesDirectContent = true; // Default to direct content management
    private bool _hasExplicitContentManagement;
    private CssContentAdapterText? _cssContentAdapter;
    private bool _cssContentAdapterAttached;
    private bool _cssContentAdapterDirty = true;

    internal TextBlock? CssContentAdapter => _cssContentAdapterAttached ? _cssContentAdapter : null;
    internal bool UsesCssContentAdapter => _cssContentAdapterAttached;
    protected virtual bool SupportsSelfDrawnCssContentAdapter => false;
    protected override bool SupportsCssInlineContentLayout => SupportsSelfDrawnCssContentAdapter && NeedsCssContentAdapter;

    internal bool NeedsCssContentAdapter => SupportsSelfDrawnCssContentAdapter &&
        Content is not null and not UIElement &&
        ContentTemplate is null && ContentTemplateSelector is null &&
        !Styling.CssReplacedContent.IsActive(this) &&
        !HasLocalValue(TemplateProperty) &&
        HasCssContentAdapterTriggers();

    private bool HasCssContentAdapterTriggers()
    {
        if (Styling.CssGeneratedContent.HasBoxes(this) ||
            CssDisplayMode != Styling.CssDisplayMode.Native ||
            CssInlineOuter ||
            Styling.CssFirstLetter.HasTargetOrAncestor(this) ||
            Styling.CssFirstLine.HasTargetOrAncestor(this) ||
            Styling.CssMultiColumnProperties.Active(this) ||
            Styling.CssLineClampProperties.Active(this) ||
            Styling.CssInitialLetterProperties.Metrics(this) is not null)
            return true;
        if (CssRuntimeState?.Applied is { Count: > 0 } applied)
        {
            foreach (var property in applied.Keys)
            {
                if (property.OwnerType == typeof(Styling.CssFlowProperties) ||
                    property.OwnerType == typeof(Styling.CssTextDecorationProperties) ||
                    property.OwnerType == typeof(Styling.CssTextEmphasis) ||
                    property.OwnerType == typeof(Styling.CssTextShadowProperties) ||
                    property.OwnerType == typeof(Styling.CssInitialLetterProperties) ||
                    property.OwnerType.Namespace == "Jalium.UI.Styling" && property.OwnerType.Name.StartsWith("CssFont", StringComparison.Ordinal) ||
                    property == Styling.CssComputedLineHeightValue.Property)
                    return true;
            }
        }
        return GetValueSourceInternal(Styling.CssFlowProperties.TextTransformProperty).BaseValueSource != BaseValueSource.Default ||
            GetValueSourceInternal(Styling.CssFlowProperties.LetterSpacingProperty).BaseValueSource != BaseValueSource.Default ||
            GetValueSourceInternal(Styling.CssFlowProperties.WordSpacingProperty).BaseValueSource != BaseValueSource.Default ||
            GetValueSourceInternal(Styling.CssFlowProperties.TextIndentProperty).BaseValueSource != BaseValueSource.Default ||
            GetValueSourceInternal(Styling.CssFlowProperties.WhiteSpaceProperty).BaseValueSource != BaseValueSource.Default ||
            GetValueSourceInternal(Styling.CssTextDecorationProperties.LineProperty).BaseValueSource != BaseValueSource.Default ||
            GetValueSourceInternal(Styling.CssTextEmphasis.StyleProperty).BaseValueSource != BaseValueSource.Default ||
            GetValueSourceInternal(Styling.CssTextShadowProperties.ValueProperty).BaseValueSource != BaseValueSource.Default;
    }

    protected override void PrepareCssContentLayout()
    {
        if (SupportsSelfDrawnCssContentAdapter && EnsureCssContentAdapter())
            return;
        base.PrepareCssContentLayout();
    }

    protected internal override void OnCssContentHostUpdated()
    {
        if (SupportsSelfDrawnCssContentAdapter && _cssContentAdapterAttached && !NeedsCssContentAdapter)
        {
            DetachCssContentAdapter();
            InvalidateMeasure();
            InvalidateArrange();
            InvalidateVisual();
        }
        base.OnCssContentHostUpdated();
    }

    internal bool EnsureCssContentAdapter()
    {
        if (!NeedsCssContentAdapter)
        {
            DetachCssContentAdapter();
            return false;
        }

        if (TemplateRootInternal is { } templateRoot && ReferenceEquals(templateRoot.VisualParent, this))
        {
            RemoveVisualChild(templateRoot);
        }

        if (_cssContentAdapter is null)
        {
            _cssContentAdapter = new CssContentAdapterText(this);
            PropertyChangedInternal += (dp, _, _) =>
            {
                if (!_cssContentAdapterAttached || _cssContentAdapter is null) return;
                if (dp == ContentProperty || dp == ContentStringFormatProperty ||
                    dp == FontFamilyProperty || dp == FontSizeProperty || dp == FontStyleProperty ||
                    dp == FontWeightProperty || dp == FontStretchProperty || dp == ForegroundProperty ||
                    dp == FlowDirectionProperty || dp == LanguageProperty ||
                    dp.OwnerType.Name.StartsWith("Css", StringComparison.Ordinal))
                {
                    _cssContentAdapterDirty = true;
                    _cssContentAdapter.InvalidateCaches();
                    _cssContentAdapter.InvalidateMeasure();
                    _cssContentAdapter.InvalidateVisual();
                    InvalidateMeasure();
                    InvalidateVisual();
                }
            };
        }

        if (!_cssContentAdapterAttached)
        {
            _cssContentAdapterAttached = true;
            if (!ReferenceEquals(_cssContentAdapter.VisualParent, this))
                AddVisualChild(_cssContentAdapter);
        }

        _cssContentAdapterDirty = true;
        SyncCssContentAdapterIfNeeded();
        return true;
    }

    internal void DetachCssContentAdapter()
    {
        if (!_cssContentAdapterAttached)
            return;
        _cssContentAdapterAttached = false;
        if (_cssContentAdapter is not null && ReferenceEquals(_cssContentAdapter.VisualParent, this))
            RemoveVisualChild(_cssContentAdapter);
        if (TemplateRootInternal is { VisualParent: null } templateRoot)
            AddVisualChild(templateRoot);
    }

    internal void RefreshCssContentAdapterState()
    {
        if (!_cssContentAdapterAttached || _cssContentAdapter is null)
            return;
        _cssContentAdapterDirty = true;
        SyncCssContentAdapterIfNeeded();
        _cssContentAdapter.InvalidateCaches();
        _cssContentAdapter.InvalidateMeasure();
        _cssContentAdapter.InvalidateVisual();
    }

    internal virtual Brush? ResolveCssAdapterForeground() => Foreground;
    internal virtual TextDecorationCollection? ResolveCssAdapterTextDecorations() => null;

    internal void SyncCssContentAdapterIfNeeded()
    {
        if (!_cssContentAdapterAttached || _cssContentAdapter is null || !_cssContentAdapterDirty)
            return;
        _cssContentAdapterDirty = false;
        SyncCssContentAdapter(_cssContentAdapter);
    }

    private void SyncCssContentAdapter(CssContentAdapterText adapter)
    {
        adapter.DocumentFontSource = this;
        adapter.IsDocumentBoxContent = true;
        adapter.IsHitTestVisible = false;
        adapter.Focusable = false;
        adapter.Text = Content is null ? string.Empty : FormatContent(Content);
        adapter.FontFamily = FontFamily;
        adapter.FontSize = FontSize;
        adapter.FontStyle = FontStyle;
        adapter.FontWeight = FontWeight;
        adapter.FontStretch = FontStretch;
        adapter.Foreground = ResolveCssAdapterForeground();
        adapter.FlowDirection = FlowDirection;
        adapter.Language = Language;
        adapter.TextDecorations = ResolveCssAdapterTextDecorations() ?? [];
        adapter.ClearValue(TextBlock.TextWrappingProperty);
        adapter.SetLayerValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap, LayerValueSource.StyleSetter);
        foreach (var property in Styling.CssPropertyRegistry.GetInheritedStorageProperties())
        {
            if (!property.ReadOnly)
                adapter.SetLayerValue(property, GetValue(property), LayerValueSource.StyleSetter);
        }
        adapter.SetValue(Styling.CssComputedLineHeightValue.Property, Styling.CssComputedLineHeightValue.Inherited(Styling.CssNode.Get(this)));
        TextBlock.CopyCssFontState(this, adapter);
        var surrounding = new Styling.CssPanelChildren(this).Enumerate().OfType<Styling.CssGeneratedText>()
            .Where(static text => text.CssLayout?.IsOutOfFlow != true && Styling.CssFloatProperties.ActiveSide(text) == Styling.CssFloatSide.None).ToArray();
        adapter.CssInlineOuter = Styling.CssDisplayLayout.IsFlow(this) &&
            (CssInlineOuter || surrounding.Length == 0 || surrounding.All(static text => text.CssInlineOuter));
    }

    internal sealed class CssContentAdapterText(ContentControl owner) : TextBlock, Styling.ICssAnonymousBox
    {
        protected override bool ParticipatesInRenderCache => false;
        internal override DependencyObject? DocumentDecorationSource => owner;
        protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => null;
        protected override bool SupportsCssInlineContentLayout => true;
        FrameworkElement Styling.ICssAnonymousBox.Owner => owner;
        double Styling.ICssAnonymousBox.BaselineOffset(double height, bool last) =>
            last ? GetLastBaselineOffset(height) : GetFirstBaselineOffset(height);
        bool Styling.ICssAnonymousBox.Contains(Point point) => false;
        protected override void PrepareCssContentLayout()
        {
            owner.SyncCssContentAdapterIfNeeded();
            base.PrepareCssContentLayout();
        }
    }

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.ContentControlAutomationPeer(this);
    }

    /// <summary>
    /// Identifies the Content dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ContentProperty =
        DependencyProperty.Register(nameof(Content), typeof(object), typeof(ContentControl),
            new PropertyMetadata(null, OnContentChanged));

    /// <summary>
    /// Identifies the ContentTemplate dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ContentTemplateProperty =
        DependencyProperty.Register(nameof(ContentTemplate), typeof(DataTemplate), typeof(ContentControl),
            new PropertyMetadata(null, OnContentTemplateChanged));

    /// <summary>
    /// Identifies the ContentTemplateSelector dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ContentTemplateSelectorProperty =
        DependencyProperty.Register(nameof(ContentTemplateSelector), typeof(DataTemplateSelector), typeof(ContentControl),
            new PropertyMetadata(null, OnContentTemplateSelectorChanged));

    /// <summary>Identifies the <see cref="ContentStringFormat"/> dependency property.</summary>
    public static readonly DependencyProperty ContentStringFormatProperty =
        DependencyProperty.Register(nameof(ContentStringFormat), typeof(string), typeof(ContentControl),
            new PropertyMetadata(null, OnContentStringFormatPropertyChanged));

    private static readonly DependencyPropertyKey HasContentPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(HasContent), typeof(bool), typeof(ContentControl),
            new PropertyMetadata(false));

    /// <summary>Identifies the read-only <see cref="HasContent"/> dependency property.</summary>
    public static readonly DependencyProperty HasContentProperty = HasContentPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the ContentTransition dependency property.
    /// When set, content changes are animated using the specified transition.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ContentTransitionProperty =
        DependencyProperty.Register(nameof(ContentTransition), typeof(ContentTransition), typeof(ContentControl),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the TransitionMode dependency property.
    /// Provides a shortcut to set common transitions without creating a ContentTransition instance.
    /// Used when <see cref="ContentTransition"/> is null.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty TransitionModeProperty =
        DependencyProperty.Register(nameof(TransitionMode), typeof(TransitionMode?), typeof(ContentControl),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the content of this control.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>
    /// Gets or sets the template used to display the content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    /// <summary>
    /// Gets or sets the DataTemplateSelector used to choose a template for content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public DataTemplateSelector? ContentTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ContentTemplateSelectorProperty);
        set => SetValue(ContentTemplateSelectorProperty, value);
    }

    /// <summary>Gets or sets the composite format string used for non-visual content.</summary>
    public string? ContentStringFormat
    {
        get => (string?)GetValue(ContentStringFormatProperty);
        set => SetValue(ContentStringFormatProperty, value);
    }

    /// <summary>Gets whether this control currently has non-null content.</summary>
    public bool HasContent => (bool)(GetValue(HasContentProperty) ?? false);

    /// <summary>
    /// Gets or sets the content transition animation.
    /// When set, content changes are animated using this transition.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ContentTransition? ContentTransition
    {
        get => (ContentTransition?)GetValue(ContentTransitionProperty);
        set => SetValue(ContentTransitionProperty, value);
    }

    /// <summary>
    /// Gets or sets the transition mode shortcut.
    /// Used when <see cref="ContentTransition"/> is null.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public TransitionMode? TransitionMode
    {
        get => (TransitionMode?)GetValue(TransitionModeProperty);
        set => SetValue(TransitionModeProperty, value);
    }

    /// <summary>
    /// Gets the content element for direct content management.
    /// </summary>
    protected UIElement? ContentElement => _contentElement;

    /// <summary>
    /// Disables direct content management. Call this in the constructor of controls
    /// that use ControlTemplate with ContentPresenter (e.g., Button).
    /// </summary>
    protected void UseTemplateContentManagement()
    {
        _hasExplicitContentManagement = true;
        SetContentManagement(useDirect: false);
    }

    private void SetContentManagement(bool useDirect)
    {
        if (useDirect)
        {
            var wasDirect = _usesDirectContent;
            _usesDirectContent = true;
            if (!wasDirect) RebuildDirectContent(Content);
            return;
        }
        if (_usesDirectContent && _contentElement is { } element)
        {
            _contentElement = null;
            RemoveVisualChild(element);
        }
        _usesDirectContent = false;
    }

    /// <summary>
    /// Enables direct content management. Call this in the constructor of controls
    /// that have their own OnRender implementation (e.g., CheckBox, RadioButton).
    /// This overrides any parent class's call to UseTemplateContentManagement().
    /// </summary>
    protected void UseDirectContentManagement()
    {
        _hasExplicitContentManagement = true;
        SetContentManagement(useDirect: true);
    }

    /// <inheritdoc />
    protected override void OnTemplateChanged(ControlTemplate oldTemplate, ControlTemplate newTemplate)
    {
        base.OnTemplateChanged(oldTemplate, newTemplate);
        // A plain ContentControl participates in its template automatically.
        // Specialized self-rendering controls can still explicitly own content.
        if (!_hasExplicitContentManagement) SetContentManagement(useDirect: newTemplate == null);
    }

    /// <summary>
    /// Gets whether this control uses direct content management.
    /// Returns true for most controls; false for controls using ControlTemplate.
    /// </summary>
    protected bool UsesDirectContent => _usesDirectContent;

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContentControl control)
        {
            control.SetValue(HasContentPropertyKey, e.NewValue != null);
            control.OnContentChanged(e.OldValue, e.NewValue);
        }
    }

    private static void OnContentTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContentControl control)
        {
            control.OnContentTemplateChanged((DataTemplate?)e.OldValue, (DataTemplate?)e.NewValue);
            control.RebuildDirectContent(control.Content);
        }
    }

    private static void OnContentTemplateSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContentControl control)
        {
            control.OnContentTemplateSelectorChanged(
                (DataTemplateSelector?)e.OldValue,
                (DataTemplateSelector?)e.NewValue);
            control.RebuildDirectContent(control.Content);
        }
    }

    private static void OnContentStringFormatPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ContentControl)d;
        control.OnContentStringFormatChanged((string?)e.OldValue, (string?)e.NewValue);
        control.RebuildDirectContent(control.Content);
    }

    /// <summary>
    /// Called when the Content property changes.
    /// </summary>
    protected virtual void OnContentChanged(object? oldContent, object? newContent)
    {
        if (!ReferenceEquals(oldContent, newContent))
        {
            RemoveLogicalChild(oldContent);
            AddLogicalChild(newContent);
        }

        RebuildDirectContent(newContent);
    }

    private void RebuildDirectContent(object? content)
    {
        if (_usesDirectContent)
        {
            // Direct content management
            if (_contentElement != null)
            {
                var element = _contentElement;
                _contentElement = null;
                RemoveVisualChild(element);
            }

            if (content is UIElement newElement)
            {
                _contentElement = newElement;
                AddVisualChild(newElement);
            }
            else if (content != null)
            {
                // Handle non-UIElement content (string, ViewModel objects, etc.)
                var element = CreateContentElementForDirectMode(content);
                if (element != null)
                {
                    _contentElement = element;
                    AddVisualChild(element);
                }
            }
        }

        InvalidateMeasure();
    }

    /// <summary>
    /// Creates a visual element for non-UIElement content in direct mode.
    /// </summary>
    private FrameworkElement? CreateContentElementForDirectMode(object content)
    {
        var template = ContentTemplate
            ?? ContentTemplateSelector?.SelectTemplate(content, this);
        if (template == null && content.GetType() != typeof(object))
        {
            template = ResourceLookup.FindImplicitDataTemplate(this, content.GetType()) as DataTemplate;
        }

        if (template != null)
        {
            var templateContent = template.LoadContent();
            if (templateContent != null)
            {
                ContentPresenter.PromoteDataTemplateLiteralsForTriggers(template, templateContent);
                templateContent.DataContext = content;
                return templateContent;
            }
        }

        // Create TextBlock for string content
        if (content is string text)
        {
            return new TextBlock { Text = FormatContent(text), Foreground = Foreground };
        }

        // For other objects, use ToString()
        return new TextBlock { Text = FormatContent(content), Foreground = Foreground };
    }

    private string FormatContent(object content)
    {
        return string.IsNullOrEmpty(ContentStringFormat)
            ? content.ToString() ?? string.Empty
            : string.Format(CultureInfo.CurrentCulture, ContentStringFormat, content);
    }

    /// <summary>Called when <see cref="ContentStringFormat"/> changes.</summary>
    protected virtual void OnContentStringFormatChanged(string? oldContentStringFormat, string? newContentStringFormat)
    {
    }

    /// <summary>Called when <see cref="ContentTemplate"/> changes.</summary>
    protected virtual void OnContentTemplateChanged(DataTemplate? oldContentTemplate, DataTemplate? newContentTemplate)
    {
    }

    /// <summary>Called when <see cref="ContentTemplateSelector"/> changes.</summary>
    protected virtual void OnContentTemplateSelectorChanged(
        DataTemplateSelector? oldContentTemplateSelector,
        DataTemplateSelector? newContentTemplateSelector)
    {
    }

    /// <summary>Adds the single object child supplied by a markup reader.</summary>
    protected virtual void AddChild(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (HasContent)
        {
            throw new InvalidOperationException("ContentControl can contain only one logical child.");
        }

        Content = value;
    }

    /// <summary>Adds literal text supplied by a markup reader.</summary>
    protected virtual void AddText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Content == null)
        {
            Content = text;
        }
        else if (Content is string existing)
        {
            Content = existing + text;
        }
        else
        {
            throw new InvalidOperationException("Text cannot be added after object content.");
        }
    }

    void IAddChild.AddChild(object value) => AddChild(value);

    void IAddChild.AddText(string text) => AddText(text);

    /// <summary>Returns whether the Content property has a local value that should be serialized.</summary>
    public virtual bool ShouldSerializeContent() => HasLocalValue(ContentProperty);

    #region Visual Children

    /// <inheritdoc />
    protected override int VisualChildrenCount
    {
        get
        {
            if (_cssContentAdapterAttached && _cssContentAdapter != null)
            {
                return 1;
            }
            if (_usesDirectContent)
            {
                return _contentElement != null ? 1 : 0;
            }
            // Template-based: use Control's implementation
            return base.VisualChildrenCount;
        }
    }

    /// <inheritdoc />
    protected override Visual? GetVisualChild(int index)
    {
        if (_cssContentAdapterAttached && _cssContentAdapter != null)
        {
            if (index == 0)
            {
                return _cssContentAdapter;
            }
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        if (_usesDirectContent)
        {
            if (index == 0 && _contentElement != null)
            {
                return _contentElement;
            }
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        // Template-based: use Control's implementation
        return base.GetVisualChild(index);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_usesDirectContent)
        {
            var padding = Padding;
            var border = BorderThickness;
            var contentSize = default(Size);
            if (_contentElement != null && _contentElement is not FrameworkElement { CssLayout.IsOutOfFlow: true })
            {
                var contentAvailable = new Size(
                    Math.Max(0, availableSize.Width - padding.TotalWidth - border.TotalWidth),
                    Math.Max(0, availableSize.Height - padding.TotalHeight - border.TotalHeight));

                if (_contentElement is FrameworkElement { CssLayout: not null })
                    Styling.CssLayoutBox.MeasureWithContainingBlock(_contentElement, contentAvailable, CssChildContainingSize(contentAvailable));
                else
                    _contentElement.Measure(contentAvailable);
                contentSize = _contentElement.DesiredSize;
            }
            MeasureCssLayoutAbsoluteChildren(availableSize);
            if (_contentElement != null)
            {
                // Negative Padding/BorderThickness are legal; the Size constructor is
                // not — clamp at the summation sink.
                return new Size(
                    Math.Max(0, contentSize.Width + padding.TotalWidth + border.TotalWidth),
                    Math.Max(0, contentSize.Height + padding.TotalHeight + border.TotalHeight));
            }
            return default(Size);
        }
        // Template-based: use Control's implementation
        return base.MeasureOverride(availableSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_usesDirectContent)
        {
            if (_contentElement is FrameworkElement fe && fe.CssLayout is not { IsOutOfFlow: true })
            {
                var padding = Padding;
                var border = BorderThickness;

                var contentRect = new Rect(
                    padding.Left + border.Left,
                    padding.Top + border.Top,
                    Math.Max(0, finalSize.Width - padding.TotalWidth - border.TotalWidth),
                    Math.Max(0, finalSize.Height - padding.TotalHeight - border.TotalHeight));

                fe.Arrange(contentRect);
            }
            ArrangeCssLayoutAbsoluteChildren(finalSize);
            return finalSize;
        }
        // Template-based: use Control's implementation
        return base.ArrangeOverride(finalSize);
    }

    #endregion
}
