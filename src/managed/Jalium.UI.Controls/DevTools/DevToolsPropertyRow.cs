using System.ComponentModel;
using System.Windows.Input;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

/// <summary>A live diagnostic property row that reuses the standard editor and keyboard contracts.</summary>
[TemplateVisualState(Name = "DefaultSource", GroupName = "SourceStates")]
[TemplateVisualState(Name = "LocalSource", GroupName = "SourceStates")]
[TemplateVisualState(Name = "StyleSource", GroupName = "SourceStates")]
[TemplateVisualState(Name = "TemplateSource", GroupName = "SourceStates")]
[TemplateVisualState(Name = "InheritedSource", GroupName = "SourceStates")]
[TemplateVisualState(Name = "AnimatedSource", GroupName = "SourceStates")]
[TemplateVisualState(Name = "UnavailableSource", GroupName = "SourceStates")]
[TemplateVisualState(Name = "SourceVisible", GroupName = "SourceVisibilityStates")]
[TemplateVisualState(Name = "SourceHidden", GroupName = "SourceVisibilityStates")]
[TemplateVisualState(Name = "Alternate", GroupName = "RowStates")]
[TemplateVisualState(Name = "Regular", GroupName = "RowStates")]
public class DevToolsPropertyRow : PropertyGridRow
{
    static DevToolsPropertyRow() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsPropertyRow), new PropertyMetadata(typeof(DevToolsPropertyRow)));
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model), typeof(DevToolsDependencyPropertyModel), typeof(DevToolsPropertyRow), new PropertyMetadata(null, OnModelChanged));
    public static readonly DependencyProperty IsAlternateProperty = DependencyProperty.Register(nameof(IsAlternate), typeof(bool), typeof(DevToolsPropertyRow), new PropertyMetadata(false, OnStateChanged));
    public static readonly DependencyProperty FontDialogCommandProperty = DependencyProperty.Register(nameof(FontDialogCommand), typeof(ICommand), typeof(DevToolsPropertyRow), new PropertyMetadata(null, OnFontCommandChanged));
    private static readonly DependencyPropertyKey IsFontFamilyPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsFontFamily), typeof(bool), typeof(DevToolsPropertyRow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsFontFamilyProperty = IsFontFamilyPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasFontDialogPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasFontDialog), typeof(bool), typeof(DevToolsPropertyRow), new PropertyMetadata(false));
    public static readonly DependencyProperty HasFontDialogProperty = HasFontDialogPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PreviewFontFamilyPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PreviewFontFamily), typeof(FontFamily), typeof(DevToolsPropertyRow), new PropertyMetadata(null));
    public static readonly DependencyProperty PreviewFontFamilyProperty = PreviewFontFamilyPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PreviewFontWeightPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PreviewFontWeight), typeof(FontWeight), typeof(DevToolsPropertyRow), new PropertyMetadata(FontWeights.Normal));
    public static readonly DependencyProperty PreviewFontWeightProperty = PreviewFontWeightPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey SourceTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SourceText), typeof(string), typeof(DevToolsPropertyRow), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty SourceTextProperty = SourceTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey OriginPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Origin), typeof(DevToolsPropertyOrigin), typeof(DevToolsPropertyRow), new PropertyMetadata(DevToolsPropertyOrigin.Default));
    public static readonly DependencyProperty OriginProperty = OriginPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PreviewBrushPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PreviewBrush), typeof(Brush), typeof(DevToolsPropertyRow), new PropertyMetadata(null));
    public static readonly DependencyProperty PreviewBrushProperty = PreviewBrushPropertyKey.DependencyProperty;
    public DevToolsDependencyPropertyModel? Model { get => (DevToolsDependencyPropertyModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public bool IsAlternate { get => (bool)GetValue(IsAlternateProperty)!; set => SetValue(IsAlternateProperty, value); }
    public ICommand? FontDialogCommand { get => (ICommand?)GetValue(FontDialogCommandProperty); set => SetValue(FontDialogCommandProperty, value); }
    public bool IsFontFamily => (bool)GetValue(IsFontFamilyProperty)!;
    public bool HasFontDialog => (bool)GetValue(HasFontDialogProperty)!;
    public FontFamily? PreviewFontFamily => (FontFamily?)GetValue(PreviewFontFamilyProperty);
    public FontWeight PreviewFontWeight => (FontWeight)GetValue(PreviewFontWeightProperty)!;
    public string SourceText => (string)GetValue(SourceTextProperty)!;
    public DevToolsPropertyOrigin Origin => (DevToolsPropertyOrigin)GetValue(OriginProperty)!;
    public Brush? PreviewBrush => (Brush?)GetValue(PreviewBrushProperty);
    private static void OnModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var row = (DevToolsPropertyRow)d;
        if (e.OldValue is DevToolsDependencyPropertyModel old) PropertyChangedEventManager.RemoveHandler(old, row.OnModelPropertyChanged, string.Empty);
        row.SetCurrentValue(PropertyProperty, (e.NewValue as DevToolsDependencyPropertyModel)?.EditorProperty);
        if (e.NewValue is DevToolsDependencyPropertyModel current) PropertyChangedEventManager.AddHandler(current, row.OnModelPropertyChanged, string.Empty);
        row.RefreshSource();
    }
    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, Model)) return;
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(DevToolsDependencyPropertyModel.EditorProperty)) SetCurrentValue(PropertyProperty, Model?.EditorProperty);
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == Model?.Property.Name || e.PropertyName is nameof(DevToolsDependencyPropertyModel.SourceText) or nameof(DevToolsDependencyPropertyModel.Origin) or nameof(DevToolsDependencyPropertyModel.PreviewColor) or nameof(DevToolsDependencyPropertyModel.EditorProperty)) RefreshSource();
    }
    private void RefreshSource()
    {
        SetValue(SourceTextPropertyKey, Model?.SourceText ?? string.Empty);
        SetValue(OriginPropertyKey, Model?.Origin ?? DevToolsPropertyOrigin.Default);
        var color = Model?.PreviewColor;
        if (color == null) SetValue(PreviewBrushPropertyKey, null);
        else if (PreviewBrush is not SolidColorBrush current || current.Color != color.Value) SetValue(PreviewBrushPropertyKey, new SolidColorBrush(color.Value));
        RefreshFontPreview();
        UpdateSourceStates();
    }
    private static void OnFontCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsPropertyRow)d).RefreshFontPreview();
    private void RefreshFontPreview()
    {
        bool family = Model?.Property.PropertyType == typeof(FontFamily);
        FontFamily previewFamily = FontFamily; FontWeight previewWeight = FontWeight;
        if (Model?.TryGetTarget(out var target) == true)
        {
            try
            {
                if (family && target.GetValue(Model.Property) is FontFamily font) previewFamily = font;
                else if (Model.Property.PropertyType == typeof(FontWeight) && target.GetValue(Model.Property) is FontWeight weight) previewWeight = weight;
            }
            catch { /* A font preview must not replace the editor's own read error. */ }
        }
        SetValue(IsFontFamilyPropertyKey, family);
        SetValue(HasFontDialogPropertyKey, family && FontDialogCommand != null && Property?.IsReadOnly == false && Origin != DevToolsPropertyOrigin.Unavailable);
        if (PreviewFontFamily?.Source != previewFamily.Source) SetValue(PreviewFontFamilyPropertyKey, new FontFamily(previewFamily.Source));
        SetValue(PreviewFontWeightPropertyKey, previewWeight);
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == FontFamilyProperty || e.Property == FontWeightProperty) RefreshFontPreview();
    }
    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsPropertyRow)d).UpdateSourceStates();
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateSourceStates(); }
    private void UpdateSourceStates()
    {
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, Origin + "Source", false);
        VisualStateManager.GoToState(this, Model == null ? "SourceHidden" : "SourceVisible", false);
        VisualStateManager.GoToState(this, IsAlternate ? "Alternate" : "Regular", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new PropertyRowPeer(this);
    private sealed class PropertyRowPeer(DevToolsPropertyRow owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => nameof(DevToolsPropertyRow);
        protected override string GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : owner.DisplayName;
    }
}
