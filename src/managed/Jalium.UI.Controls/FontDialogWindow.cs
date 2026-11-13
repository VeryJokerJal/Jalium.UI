using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>A reusable, templated font-selection window. Editing is committed to the dialog only after an accepted close.</summary>
[TemplatePart(Name = "PART_FontFamily", Type = typeof(ComboBox))]
[TemplatePart(Name = "PART_FontSize", Type = typeof(TextBox))]
[TemplatePart(Name = "PART_Color", Type = typeof(TextBox))]
[TemplatePart(Name = "PART_Bold", Type = typeof(CheckBox))]
[TemplatePart(Name = "PART_Italic", Type = typeof(CheckBox))]
[TemplatePart(Name = "PART_Underline", Type = typeof(CheckBox))]
[TemplatePart(Name = "PART_Strikeout", Type = typeof(CheckBox))]
[TemplatePart(Name = "PART_AcceptButton", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_CancelButton", Type = typeof(ButtonBase))]
[TemplateVisualState(Name = "Valid", GroupName = "ValidationStates")]
[TemplateVisualState(Name = "Invalid", GroupName = "ValidationStates")]
[TemplateVisualState(Name = "ColorVisible", GroupName = "ColorStates")]
[TemplateVisualState(Name = "ColorHidden", GroupName = "ColorStates")]
[TemplateVisualState(Name = "EffectsVisible", GroupName = "EffectsStates")]
[TemplateVisualState(Name = "EffectsHidden", GroupName = "EffectsStates")]
public sealed class FontDialogWindow : Window
{
    public static readonly DependencyProperty SelectedFontFamilyProperty = SelectionProperty<FontFamily?>(nameof(SelectedFontFamily), null);
    public static readonly DependencyProperty SelectedFontSizeProperty = DependencyProperty.Register(
        nameof(SelectedFontSize), typeof(double), typeof(FontDialogWindow), new FrameworkPropertyMetadata(12d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectionChanged,
            (d, value) => Math.Clamp(Finite(value, 12), ((FontDialogWindow)d).MinimumFontSize,
                Math.Max(((FontDialogWindow)d).MinimumFontSize, ((FontDialogWindow)d).MaximumFontSize))));
    public static readonly DependencyProperty SelectedFontWeightProperty = SelectionProperty(nameof(SelectedFontWeight), FontWeights.Normal);
    public static readonly DependencyProperty SelectedFontStyleProperty = SelectionProperty(nameof(SelectedFontStyle), FontStyles.Normal);
    public static readonly DependencyProperty SelectedFontStretchProperty = SelectionProperty(nameof(SelectedFontStretch), FontStretches.Normal);
    public static readonly DependencyProperty SelectedTextDecorationsProperty = SelectionProperty<TextDecorationCollection?>(nameof(SelectedTextDecorations), null);
    public static readonly DependencyProperty SelectedColorProperty = SelectionProperty(nameof(SelectedColor), Color.Black);
    public static readonly DependencyProperty FontFamilyNameProperty = EditorProperty(nameof(FontFamilyName), string.Empty);
    public static readonly DependencyProperty FontSizeTextProperty = EditorProperty(nameof(FontSizeText), "12");
    public static readonly DependencyProperty ColorTextProperty = EditorProperty(nameof(ColorText), "#FF000000");
    public static readonly DependencyProperty IsBoldProperty = EditorProperty(nameof(IsBold), false);
    public static readonly DependencyProperty IsItalicProperty = EditorProperty(nameof(IsItalic), false);
    public static readonly DependencyProperty IsUnderlineProperty = EditorProperty(nameof(IsUnderline), false);
    public static readonly DependencyProperty IsStrikeoutProperty = EditorProperty(nameof(IsStrikeout), false);
    public static readonly DependencyProperty ShowColorProperty = DependencyProperty.Register(
        nameof(ShowColor), typeof(bool), typeof(FontDialogWindow), new PropertyMetadata(true, OnOptionsChanged));
    public static readonly DependencyProperty ShowEffectsProperty = DependencyProperty.Register(
        nameof(ShowEffects), typeof(bool), typeof(FontDialogWindow), new PropertyMetadata(true, OnOptionsChanged));
    public static readonly DependencyProperty FixedPitchOnlyProperty = DependencyProperty.Register(
        nameof(FixedPitchOnly), typeof(bool), typeof(FontDialogWindow), new PropertyMetadata(false, OnOptionsChanged));
    public static readonly DependencyProperty SampleTextProperty = DependencyProperty.Register(
        nameof(SampleText), typeof(string), typeof(FontDialogWindow), new PropertyMetadata("AaBbYyZz"));
    public static readonly DependencyProperty MinimumFontSizeProperty = DependencyProperty.Register(
        nameof(MinimumFontSize), typeof(double), typeof(FontDialogWindow), new PropertyMetadata(1d, OnConstraintsChanged,
            (_, value) => Math.Max(1, Finite(value, 1))));
    public static readonly DependencyProperty MaximumFontSizeProperty = DependencyProperty.Register(
        nameof(MaximumFontSize), typeof(double), typeof(FontDialogWindow), new PropertyMetadata(500d, OnConstraintsChanged,
            (d, value) => Math.Max(((FontDialogWindow)d).MinimumFontSize, Finite(value, 500))));
    private static readonly DependencyPropertyKey FontNamesPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(FontNames), typeof(IReadOnlyList<string>), typeof(FontDialogWindow), new PropertyMetadata(Array.Empty<string>()));
    public static readonly DependencyProperty FontNamesProperty = FontNamesPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey CanAcceptPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CanAccept), typeof(bool), typeof(FontDialogWindow), new PropertyMetadata(false));
    public static readonly DependencyProperty CanAcceptProperty = CanAcceptPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey ValidationMessagePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ValidationMessage), typeof(string), typeof(FontDialogWindow), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ValidationMessageProperty = ValidationMessagePropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PreviewForegroundPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PreviewForeground), typeof(Brush), typeof(FontDialogWindow), new PropertyMetadata(null));
    public static readonly DependencyProperty PreviewForegroundProperty = PreviewForegroundPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PreviewBackgroundPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PreviewBackground), typeof(Brush), typeof(FontDialogWindow), new PropertyMetadata(null));
    public static readonly DependencyProperty PreviewBackgroundProperty = PreviewBackgroundPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey MaxContentHeightPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(MaxContentHeight), typeof(double), typeof(FontDialogWindow), new PropertyMetadata(600d));
    public static readonly DependencyProperty MaxContentHeightProperty = MaxContentHeightPropertyKey.DependencyProperty;

    public FontFamily? SelectedFontFamily { get => (FontFamily?)GetValue(SelectedFontFamilyProperty); set => SetValue(SelectedFontFamilyProperty, value); }
    public double SelectedFontSize { get => (double)GetValue(SelectedFontSizeProperty)!; set => SetValue(SelectedFontSizeProperty, value); }
    public FontWeight SelectedFontWeight { get => (FontWeight)GetValue(SelectedFontWeightProperty)!; set => SetValue(SelectedFontWeightProperty, value); }
    public FontStyle SelectedFontStyle { get => (FontStyle)GetValue(SelectedFontStyleProperty)!; set => SetValue(SelectedFontStyleProperty, value); }
    public FontStretch SelectedFontStretch { get => (FontStretch)GetValue(SelectedFontStretchProperty)!; set => SetValue(SelectedFontStretchProperty, value); }
    public TextDecorationCollection? SelectedTextDecorations { get => (TextDecorationCollection?)GetValue(SelectedTextDecorationsProperty); set => SetValue(SelectedTextDecorationsProperty, value); }
    public Color SelectedColor { get => (Color)GetValue(SelectedColorProperty)!; set => SetValue(SelectedColorProperty, value); }
    public string FontFamilyName { get => (string?)GetValue(FontFamilyNameProperty) ?? string.Empty; set => SetValue(FontFamilyNameProperty, value); }
    public string FontSizeText { get => (string?)GetValue(FontSizeTextProperty) ?? string.Empty; set => SetValue(FontSizeTextProperty, value); }
    public string ColorText { get => (string?)GetValue(ColorTextProperty) ?? string.Empty; set => SetValue(ColorTextProperty, value); }
    public bool IsBold { get => (bool)GetValue(IsBoldProperty)!; set => SetValue(IsBoldProperty, value); }
    public bool IsItalic { get => (bool)GetValue(IsItalicProperty)!; set => SetValue(IsItalicProperty, value); }
    public bool IsUnderline { get => (bool)GetValue(IsUnderlineProperty)!; set => SetValue(IsUnderlineProperty, value); }
    public bool IsStrikeout { get => (bool)GetValue(IsStrikeoutProperty)!; set => SetValue(IsStrikeoutProperty, value); }
    public bool ShowColor { get => (bool)GetValue(ShowColorProperty)!; set => SetValue(ShowColorProperty, value); }
    public bool ShowEffects { get => (bool)GetValue(ShowEffectsProperty)!; set => SetValue(ShowEffectsProperty, value); }
    public bool FixedPitchOnly { get => (bool)GetValue(FixedPitchOnlyProperty)!; set => SetValue(FixedPitchOnlyProperty, value); }
    public string SampleText { get => (string?)GetValue(SampleTextProperty) ?? string.Empty; set => SetValue(SampleTextProperty, value); }
    public double MinimumFontSize { get => (double)GetValue(MinimumFontSizeProperty)!; set => SetValue(MinimumFontSizeProperty, value); }
    public double MaximumFontSize { get => (double)GetValue(MaximumFontSizeProperty)!; set => SetValue(MaximumFontSizeProperty, value); }
    public IReadOnlyList<string> FontNames => (IReadOnlyList<string>)GetValue(FontNamesProperty)!;
    public bool CanAccept => (bool)GetValue(CanAcceptProperty)!;
    public string ValidationMessage => (string)GetValue(ValidationMessageProperty)!;
    public Brush? PreviewForeground => (Brush?)GetValue(PreviewForegroundProperty);
    public Brush? PreviewBackground => (Brush?)GetValue(PreviewBackgroundProperty);
    public double MaxContentHeight => (double)GetValue(MaxContentHeightProperty)!;

    private readonly FontDialog _target;
    private readonly List<(DependencyObject Editor, DependencyProperty Property)> _editors = [];
    private ButtonBase? _accept, _cancel;
    private ComboBox? _family;
    private bool _updating, _requestingAccept;
    private Selection? _pendingAcceptance;

    public FontDialogWindow() : this(new FontDialog()) { }
    public FontDialogWindow(FontDialog target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        DefaultStyleKey = typeof(FontDialogWindow);
        Title = "Choose Font";
        Width = Math.Min(520, Math.Max(320, SystemParameters.WorkArea.Width - 32));
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        SetValue(MaxContentHeightPropertyKey, Math.Max(180, SystemParameters.WorkArea.Height - 160));
        _updating = true;
        try
        {
            MinimumFontSize = target.MinSize; MaximumFontSize = target.MaxSize;
            SelectedFontFamily = target.FontFamily ?? new FontFamily(FrameworkElement.DefaultFontFamilyName);
            SelectedFontSize = target.FontSize; SelectedFontWeight = target.FontWeight;
            SelectedFontStyle = target.FontStyle; SelectedFontStretch = target.FontStretch;
            SelectedTextDecorations = target.TextDecorations;
            SelectedColor = target.Color; ShowColor = target.ShowColor; ShowEffects = target.ShowEffects;
            FixedPitchOnly = target.FixedPitchOnly; SampleText = target.SampleText;
        }
        finally { _updating = false; }
        RebuildFontNames();
        SynchronizeEditors();
        Refresh();
        Closing += OnClosing;
        Closed += OnClosed;
        Loaded += (_, _) => _family?.Focus();
        KeyDown += (_, e) =>
        {
            if (!e.Handled && e.Key == Key.Escape) { Cancel(); e.Handled = true; }
        };
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _family = GetTemplateChild("PART_FontFamily") as ComboBox;
        TrackEditor("PART_FontFamily", ComboBox.TextProperty);
        TrackEditor("PART_FontSize", TextBox.TextProperty);
        TrackEditor("PART_Color", TextBox.TextProperty);
        foreach (var name in new[] { "PART_Bold", "PART_Italic", "PART_Underline", "PART_Strikeout" })
            TrackEditor(name, ToggleButton.IsCheckedProperty);
        _accept = GetTemplateChild("PART_AcceptButton") as ButtonBase;
        _cancel = GetTemplateChild("PART_CancelButton") as ButtonBase;
        if (_accept != null) _accept.Click += OnAccept;
        if (_cancel != null) _cancel.Click += OnCancel;
        Refresh();
    }
    protected override void OnTemplateChanging()
    {
        foreach (var (editor, property) in _editors) editor.ClearBinding(property);
        _editors.Clear();
        if (_accept != null) _accept.Click -= OnAccept;
        if (_cancel != null) _cancel.Click -= OnCancel;
        _accept = _cancel = null; _family = null;
        base.OnTemplateChanging();
    }
    private void TrackEditor(string name, DependencyProperty property)
    {
        if (GetTemplateChild(name) is DependencyObject editor) _editors.Add((editor, property));
    }
    private static DependencyProperty SelectionProperty<T>(string name, T value) => DependencyProperty.Register(name, typeof(T), typeof(FontDialogWindow),
        new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectionChanged));
    private static DependencyProperty EditorProperty<T>(string name, T value) => DependencyProperty.Register(name, typeof(T), typeof(FontDialogWindow),
        new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnEditorChanged));
    private static double Finite(object? value, double fallback) => value is double number && double.IsFinite(number) ? number : fallback;

    private static void OnSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var window = (FontDialogWindow)d;
        if (window._updating) return;
        window.SynchronizeEditors();
        window.Refresh();
    }
    private static void OnEditorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var window = (FontDialogWindow)d;
        if (window._updating) return;
        window._updating = true;
        try
        {
            if (e.Property == FontFamilyNameProperty && !string.IsNullOrWhiteSpace(window.FontFamilyName))
                window.SetCurrentValue(SelectedFontFamilyProperty, new FontFamily(window.FontFamilyName.Trim()));
            else if (e.Property == FontSizeTextProperty && FontDialog.TryParseFontSize(window.FontSizeText, out var size) && double.IsFinite(size) && size > 0)
                window.SetCurrentValue(SelectedFontSizeProperty, size);
            else if (e.Property == ColorTextProperty && FontDialog.TryParseDialogColor(window.ColorText, out var color))
                window.SetCurrentValue(SelectedColorProperty, color);
            else if (e.Property == IsBoldProperty)
                window.SetCurrentValue(SelectedFontWeightProperty, window.IsBold ? FontWeights.Bold : FontWeights.Normal);
            else if (e.Property == IsItalicProperty)
                window.SetCurrentValue(SelectedFontStyleProperty, window.IsItalic ? FontStyles.Italic : FontStyles.Normal);
            else if (e.Property == IsUnderlineProperty || e.Property == IsStrikeoutProperty)
                window.SetCurrentValue(SelectedTextDecorationsProperty,
                    FontDialog.UpdateDialogTextDecorations(window.SelectedTextDecorations, window.IsUnderline, window.IsStrikeout));
        }
        finally { window._updating = false; }
        window.Refresh();
    }
    private static void OnOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var window = (FontDialogWindow)d;
        if (window._updating) return;
        if (e.Property == FixedPitchOnlyProperty) window.RebuildFontNames();
        window.Refresh();
    }
    private static void OnConstraintsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var window = (FontDialogWindow)d;
        if (window._updating) return;
        window.CoerceValue(MaximumFontSizeProperty);
        window.CoerceValue(SelectedFontSizeProperty);
        window.SynchronizeEditors();
        window.Refresh();
    }
    private void SynchronizeEditors()
    {
        _updating = true;
        try
        {
            SetCurrentValue(FontFamilyNameProperty, SelectedFontFamily?.Source ?? string.Empty);
            SetCurrentValue(FontSizeTextProperty, SelectedFontSize.ToString("0.##", CultureInfo.InvariantCulture));
            SetCurrentValue(ColorTextProperty, SelectedColor.ToString());
            SetCurrentValue(IsBoldProperty, SelectedFontWeight >= FontWeights.Bold);
            SetCurrentValue(IsItalicProperty, SelectedFontStyle == FontStyles.Italic || SelectedFontStyle == FontStyles.Oblique);
            var effects = FontDialog.GetDialogEffects(SelectedTextDecorations);
            SetCurrentValue(IsUnderlineProperty, effects.Underline); SetCurrentValue(IsStrikeoutProperty, effects.Strikeout);
        }
        finally { _updating = false; }
    }
    private void RebuildFontNames() => SetValue(FontNamesPropertyKey, FontDialog.GetFontFamilies()
        .Select(family => family.Source).Where(name => !FixedPitchOnly || LooksFixedPitch(name)).ToArray());
    private static bool LooksFixedPitch(string name) => new[] { "Mono", "Code", "Console", "Courier", "Menlo" }
        .Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));
    private void Refresh()
    {
        var error = string.Empty;
        if (string.IsNullOrWhiteSpace(FontFamilyName)) error = "Choose a font family.";
        else if (FixedPitchOnly && !FontNames.Contains(FontFamilyName, StringComparer.OrdinalIgnoreCase)) error = "Choose a fixed-pitch font from the list.";
        else if (!FontDialog.TryParseFontSize(FontSizeText, out var size) || !double.IsFinite(size) || size < MinimumFontSize || size > MaximumFontSize)
            error = $"Size must be between {MinimumFontSize:0.##} and {MaximumFontSize:0.##}.";
        else if (ShowColor && !FontDialog.TryParseDialogColor(ColorText, out _)) error = "Enter a valid color.";
        SetValue(ValidationMessagePropertyKey, error);
        SetValue(CanAcceptPropertyKey, error.Length == 0);
        SetValue(PreviewForegroundPropertyKey, new SolidColorBrush(SelectedColor));
        // The preview shows the selected ink, so its canvas must provide contrast
        // independently of the surrounding application theme.
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        var luminance = .2126 * Linear(SelectedColor.R) + .7152 * Linear(SelectedColor.G) + .0722 * Linear(SelectedColor.B);
        SetValue(PreviewBackgroundPropertyKey, 1.05 / (luminance + .05) >= (luminance + .05) / .05 ? Brushes.White : Brushes.Black);
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, CanAccept ? "Valid" : "Invalid", false);
        VisualStateManager.GoToState(this, ShowColor ? "ColorVisible" : "ColorHidden", false);
        VisualStateManager.GoToState(this, ShowEffects ? "EffectsVisible" : "EffectsHidden", false);
    }
    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (!CanAccept) return;
        _pendingAcceptance = new(SelectedFontFamily, SelectedFontSize, SelectedFontWeight, SelectedFontStyle,
            SelectedFontStretch, SelectedTextDecorations, SelectedColor);
        _requestingAccept = true;
        try { var modal = IsModal; DialogResult = true; if (!modal) Close(); }
        finally { _requestingAccept = false; }
    }
    private void OnCancel(object sender, RoutedEventArgs e) => Cancel();
    private void Cancel()
    {
        _pendingAcceptance = null;
        var modal = IsModal;
        DialogResult = false;
        if (!modal) Close();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_requestingAccept) return;
        _pendingAcceptance = null;
        if (DialogResult == true) DialogResult = null;
    }
    private void OnClosed(object? sender, EventArgs e)
    {
        if (DialogResult != true || _pendingAcceptance is not { } selection) return;
        _target.FontFamily = selection.Family; _target.FontSize = selection.Size;
        _target.FontWeight = selection.Weight; _target.FontStyle = selection.Style; _target.FontStretch = selection.Stretch;
        _target.TextDecorations = selection.Decorations;
        if (ShowColor) _target.Color = selection.Color;
        _pendingAcceptance = null;
    }
    private sealed record Selection(FontFamily? Family, double Size, FontWeight Weight, FontStyle Style,
        FontStretch Stretch, TextDecorationCollection? Decorations, Color Color);
}
