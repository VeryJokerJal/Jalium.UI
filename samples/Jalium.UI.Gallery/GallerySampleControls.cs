using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;

namespace Jalium.UI.Gallery;

/// <summary>Declares the shared sample theme independently of the gallery window.</summary>
public static class GallerySampleTheme
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        ThemeLoader.Initialize();
        var assembly = typeof(GallerySampleSurface).Assembly;
        using var stream = assembly.GetManifestResourceStream("Jalium.UI.Gallery.Themes.SampleGallery.jalxaml")
            ?? throw new InvalidOperationException("Missing sample gallery theme.");
        return ThemeManager.XamlLoader!(stream, "Themes/SampleGallery.jalxaml", assembly)!;
    });

    public static void EnsureRegistered()
    {
        var resources = Defaults.Value;
        ThemeManager.RegisterControlThemes(resources);
        if (Application.Current is { } application && !application.Resources.MergedDictionaries.Contains(resources))
            application.Resources.MergedDictionaries.Add(resources);
    }
}

/// <summary>A scrollable sample catalog; items can be appended at dispatcher background priority.</summary>
[TemplatePart(Name = "PART_ScrollViewer", Type = typeof(ScrollViewer))]
[TemplatePart(Name = "PART_ItemsPresenter", Type = typeof(ItemsPresenter))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class GallerySampleSurface : ItemsControl
{
    public GallerySampleSurface() { GallerySampleTheme.EnsureRegistered(); DefaultStyleKey = typeof(GallerySampleSurface); }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateState(); }
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue) { base.OnIsEnabledChanged(oldValue, newValue); UpdateState(); }
    private void UpdateState() => VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
}

/// <summary>A category header and wrapping collection of independently templated sample cards.</summary>
[TemplatePart(Name = "PART_ItemsPresenter", Type = typeof(ItemsPresenter))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "HasDescription", GroupName = "DescriptionStates")]
[TemplateVisualState(Name = "NoDescription", GroupName = "DescriptionStates")]
public class GallerySampleSection : ItemsControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(GallerySampleSection), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(GallerySampleSection), new PropertyMetadata(string.Empty, (d, _) => ((GallerySampleSection)d).UpdateState()));
    public string Title { get => (string)GetValue(TitleProperty)!; set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty)!; set => SetValue(DescriptionProperty, value); }
    public GallerySampleSection() { GallerySampleTheme.EnsureRegistered(); DefaultStyleKey = typeof(GallerySampleSection); }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateState(); }
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue) { base.OnIsEnabledChanged(oldValue, newValue); UpdateState(); }
    private void UpdateState()
    {
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, string.IsNullOrEmpty(Description) ? "NoDescription" : "HasDescription", false);
    }
}

/// <summary>A titled demo card which keeps fixed-size examples reachable in narrow viewports.</summary>
[TemplatePart(Name = "PART_HeaderPresenter", Type = typeof(ContentPresenter))]
[TemplatePart(Name = "PART_ContentPresenter", Type = typeof(ContentPresenter))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class GallerySampleCard : HeaderedContentControl
{
    public static readonly DependencyProperty ContentScrollBarVisibilityProperty = DependencyProperty.Register(nameof(ContentScrollBarVisibility), typeof(ScrollBarVisibility), typeof(GallerySampleCard), new PropertyMetadata(ScrollBarVisibility.Auto));
    public static readonly DependencyProperty PreferredWidthProperty = DependencyProperty.Register(nameof(PreferredWidth), typeof(double), typeof(GallerySampleCard),
        new PropertyMetadata(300d, (d, _) => ((GallerySampleCard)d).InvalidateMeasure()),
        value => value is double width && (double.IsNaN(width) || double.IsFinite(width) && width > 0));
    public double PreferredWidth { get => (double)GetValue(PreferredWidthProperty)!; set => SetValue(PreferredWidthProperty, value); }
    public ScrollBarVisibility ContentScrollBarVisibility { get => (ScrollBarVisibility)GetValue(ContentScrollBarVisibilityProperty)!; set => SetValue(ContentScrollBarVisibilityProperty, value); }
    public GallerySampleCard() { GallerySampleTheme.EnsureRegistered(); DefaultStyleKey = typeof(GallerySampleCard); }
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = PreferredWidth;
        if (double.IsNaN(width)) return base.MeasureOverride(availableSize);
        if (double.IsFinite(availableSize.Width)) width = Math.Min(width, Math.Max(0, availableSize.Width));
        var desired = base.MeasureOverride(new Size(width, availableSize.Height));
        return new Size(width, desired.Height);
    }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateState(); }
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue) { base.OnIsEnabledChanged(oldValue, newValue); UpdateState(); }
    private void UpdateState() => VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
}

[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class GallerySampleHeader : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(GallerySampleHeader), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(GallerySampleHeader), new PropertyMetadata(string.Empty));
    public string Title { get => (string)GetValue(TitleProperty)!; set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty)!; set => SetValue(DescriptionProperty, value); }
    public GallerySampleHeader() { GallerySampleTheme.EnsureRegistered(); DefaultStyleKey = typeof(GallerySampleHeader); }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false); }
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue) { base.OnIsEnabledChanged(oldValue, newValue); VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false); }
}

/// <summary>Describes a sample that requires an external resource without starting that resource.</summary>
public class GallerySamplePlaceholder : GallerySampleHeader
{
    public GallerySamplePlaceholder() => DefaultStyleKey = typeof(GallerySamplePlaceholder);
}

/// <summary>Accessible progress text for deferred sample construction.</summary>
public class GallerySampleStatus : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(GallerySampleStatus), new PropertyMetadata(string.Empty));
    public string Text { get => (string)GetValue(TextProperty)!; set => SetValue(TextProperty, value); }
    public GallerySampleStatus() { GallerySampleTheme.EnsureRegistered(); DefaultStyleKey = typeof(GallerySampleStatus); }
}
