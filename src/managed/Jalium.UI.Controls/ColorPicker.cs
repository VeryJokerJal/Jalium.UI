using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;
/// <summary>
/// Represents a control that allows the user to select a color.
/// </summary>
[TemplatePart(Name = "PART_Spectrum", Type = typeof(ColorSpectrum))]
[TemplatePart(Name = "PART_HueSlider", Type = typeof(ColorRamp))]
[TemplatePart(Name = "PART_AlphaSlider", Type = typeof(ColorRamp))]
[TemplatePart(Name = "PART_HexTextBox", Type = typeof(TextBox))]
[TemplateVisualState(Name = "Expanded", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Compact", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Valid", GroupName = "ValidationStates")]
[TemplateVisualState(Name = "Invalid", GroupName = "ValidationStates")]
public class ColorPicker : Control
{
    /// <inheritdoc/>
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.ColorPickerAutomationPeer(this);
    }

#region Dependency Properties
    /// <summary>
    /// Identifies the Color dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(Color), typeof(ColorPicker), new PropertyMetadata(Color.White, OnColorChanged));
    /// <summary>
    /// Identifies the IsAlphaEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsAlphaEnabledProperty = DependencyProperty.Register(nameof(IsAlphaEnabled), typeof(bool), typeof(ColorPicker), new PropertyMetadata(true, OnLayoutPropertyChanged));
    /// <summary>
    /// Identifies the IsColorSpectrumVisible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsColorSpectrumVisibleProperty = DependencyProperty.Register(nameof(IsColorSpectrumVisible), typeof(bool), typeof(ColorPicker), new PropertyMetadata(true, OnLayoutPropertyChanged));
    /// <summary>
    /// Identifies the IsColorSliderVisible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsColorSliderVisibleProperty = DependencyProperty.Register(nameof(IsColorSliderVisible), typeof(bool), typeof(ColorPicker), new PropertyMetadata(true, OnLayoutPropertyChanged));
    /// <summary>
    /// Identifies the IsColorPreviewVisible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsColorPreviewVisibleProperty = DependencyProperty.Register(nameof(IsColorPreviewVisible), typeof(bool), typeof(ColorPicker), new PropertyMetadata(true, OnLayoutPropertyChanged));
    /// <summary>
    /// Identifies the IsHexInputVisible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsHexInputVisibleProperty = DependencyProperty.Register(nameof(IsHexInputVisible), typeof(bool), typeof(ColorPicker), new PropertyMetadata(true, OnLayoutPropertyChanged));
    /// <summary>
    /// Identifies the ColorSpectrumShape dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ColorSpectrumShapeProperty = DependencyProperty.Register(nameof(ColorSpectrumShape), typeof(ColorSpectrumShape), typeof(ColorPicker), new PropertyMetadata(ColorSpectrumShape.Box, OnLayoutPropertyChanged));
    /// <summary>
    /// Identifies the IsCompact dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(ColorPicker), new PropertyMetadata(false, OnLayoutPropertyChanged));
#endregion
#region Routed Events
    /// <summary>
    /// Identifies the ColorChanged routed event.
    /// </summary>
    public static readonly RoutedEvent ColorChangedEvent = EventManager.RegisterRoutedEvent(nameof(ColorChanged), RoutingStrategy.Bubble, typeof(EventHandler<ColorChangedEventArgs>), typeof(ColorPicker));
    /// <summary>
    /// Occurs when the color changes.
    /// </summary>
    public event EventHandler<ColorChangedEventArgs> ColorChanged { add => AddHandler(ColorChangedEvent, value); remove => RemoveHandler(ColorChangedEvent, value); }

#endregion
#region CLR Properties
    /// <summary>
    /// Gets or sets the selected color.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Color Color { get => (Color)GetValue(ColorProperty)!; set => SetValue(ColorProperty, value); }

    /// <summary>
    /// Gets or sets whether the alpha channel can be edited.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsAlphaEnabled { get => (bool)GetValue(IsAlphaEnabledProperty)!; set => SetValue(IsAlphaEnabledProperty, value); }

    /// <summary>
    /// Gets or sets whether the color spectrum is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsColorSpectrumVisible { get => (bool)GetValue(IsColorSpectrumVisibleProperty)!; set => SetValue(IsColorSpectrumVisibleProperty, value); }

    /// <summary>
    /// Gets or sets whether the color slider is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsColorSliderVisible { get => (bool)GetValue(IsColorSliderVisibleProperty)!; set => SetValue(IsColorSliderVisibleProperty, value); }

    /// <summary>
    /// Gets or sets whether the color preview is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsColorPreviewVisible { get => (bool)GetValue(IsColorPreviewVisibleProperty)!; set => SetValue(IsColorPreviewVisibleProperty, value); }

    /// <summary>
    /// Gets or sets whether the hex input is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsHexInputVisible { get => (bool)GetValue(IsHexInputVisibleProperty)!; set => SetValue(IsHexInputVisibleProperty, value); }

    /// <summary>
    /// Gets or sets the shape of the color spectrum.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public ColorSpectrumShape ColorSpectrumShape { get => (ColorSpectrumShape)(GetValue(ColorSpectrumShapeProperty) ?? ColorSpectrumShape.Box); set => SetValue(ColorSpectrumShapeProperty, value); }

    /// <summary>
    /// Gets or sets whether the color picker is displayed in compact mode.
    /// In compact mode, the spectrum is smaller and the preview/hex input are hidden.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsCompact { get => (bool)GetValue(IsCompactProperty)!; set => SetValue(IsCompactProperty, value); }

#endregion
    public static readonly DependencyProperty HueProperty = DependencyProperty.Register(nameof(Hue), typeof(double), typeof(ColorPicker), new PropertyMetadata(0.0, OnComponentChanged, CoerceHue));
    public static readonly DependencyProperty SaturationProperty = DependencyProperty.Register(nameof(Saturation), typeof(double), typeof(ColorPicker), new PropertyMetadata(0.0, OnComponentChanged, CoerceUnit));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(ColorPicker), new PropertyMetadata(1.0, OnComponentChanged, CoerceUnit));
    public static readonly DependencyProperty AlphaProperty = DependencyProperty.Register(nameof(Alpha), typeof(double), typeof(ColorPicker), new PropertyMetadata(255.0, OnComponentChanged, CoerceAlpha));
    public static readonly DependencyProperty HexTextProperty = DependencyProperty.Register(nameof(HexText), typeof(string), typeof(ColorPicker), new PropertyMetadata(string.Empty, OnHexTextChanged));
    public static readonly DependencyProperty ErrorForegroundProperty = DependencyProperty.Register(nameof(ErrorForeground), typeof(Brush), typeof(ColorPicker), new PropertyMetadata(null));
    /// <summary>Gets or sets the brush used for inline hex validation messages.</summary>
    public Brush? ErrorForeground { get => (Brush? )GetValue(ErrorForegroundProperty); set => SetValue(ErrorForegroundProperty, value); }
    public double Hue { get => (double)GetValue(HueProperty)!; set => SetValue(HueProperty, value); }
    public double Saturation { get => (double)GetValue(SaturationProperty)!; set => SetValue(SaturationProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty)!; set => SetValue(ValueProperty, value); }
    public double Alpha { get => (double)GetValue(AlphaProperty)!; set => SetValue(AlphaProperty, value); }
    public string HexText { get => (string)GetValue(HexTextProperty)!; set => SetValue(HexTextProperty, value); }

    private static readonly DependencyPropertyKey HasHexErrorPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasHexError), typeof(bool), typeof(ColorPicker), new PropertyMetadata(false));
    public static readonly DependencyProperty HasHexErrorProperty = HasHexErrorPropertyKey.DependencyProperty;
    public bool HasHexError => (bool)GetValue(HasHexErrorProperty)!;

    private static readonly DependencyPropertyKey HexErrorTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HexErrorText), typeof(string), typeof(ColorPicker), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty HexErrorTextProperty = HexErrorTextPropertyKey.DependencyProperty;
    public string HexErrorText => (string)GetValue(HexErrorTextProperty)!;

    private static readonly DependencyPropertyKey ShowPreviewPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ShowPreview), typeof(bool), typeof(ColorPicker), new PropertyMetadata(true));
    public static readonly DependencyProperty ShowPreviewProperty = ShowPreviewPropertyKey.DependencyProperty;
    public bool ShowPreview => (bool)GetValue(ShowPreviewProperty)!;

    private static readonly DependencyPropertyKey ShowHexInputPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ShowHexInput), typeof(bool), typeof(ColorPicker), new PropertyMetadata(true));
    public static readonly DependencyProperty ShowHexInputProperty = ShowHexInputPropertyKey.DependencyProperty;
    public bool ShowHexInput => (bool)GetValue(ShowHexInputProperty)!;

    private static readonly DependencyPropertyKey ShowAlphaSliderPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ShowAlphaSlider), typeof(bool), typeof(ColorPicker), new PropertyMetadata(true));
    public static readonly DependencyProperty ShowAlphaSliderProperty = ShowAlphaSliderPropertyKey.DependencyProperty;
    public bool ShowAlphaSlider => (bool)GetValue(ShowAlphaSliderProperty)!;

    private static readonly DependencyPropertyKey EffectiveSpectrumSizePropertyKey = DependencyProperty.RegisterReadOnly(nameof(EffectiveSpectrumSize), typeof(double), typeof(ColorPicker), new PropertyMetadata(200.0));
    public static readonly DependencyProperty EffectiveSpectrumSizeProperty = EffectiveSpectrumSizePropertyKey.DependencyProperty;
    public double EffectiveSpectrumSize => (double)GetValue(EffectiveSpectrumSizeProperty)!;

    private static readonly DependencyPropertyKey EffectiveSliderHeightPropertyKey = DependencyProperty.RegisterReadOnly(nameof(EffectiveSliderHeight), typeof(double), typeof(ColorPicker), new PropertyMetadata(20.0));
    public static readonly DependencyProperty EffectiveSliderHeightProperty = EffectiveSliderHeightPropertyKey.DependencyProperty;
    public double EffectiveSliderHeight => (double)GetValue(EffectiveSliderHeightProperty)!;

    private static readonly DependencyPropertyKey EffectiveSpacingPropertyKey = DependencyProperty.RegisterReadOnly(nameof(EffectiveSpacing), typeof(double), typeof(ColorPicker), new PropertyMetadata(8.0));
    public static readonly DependencyProperty EffectiveSpacingProperty = EffectiveSpacingPropertyKey.DependencyProperty;
    public double EffectiveSpacing => (double)GetValue(EffectiveSpacingProperty)!;

    private TextBox? _hexTextBox;
    private bool _synchronizingComponents, _isUpdatingColor, _updatingHex, _hasHexDraft;
    public ColorPicker()
    {
        Focusable = true;
        SetCurrentValue(TransitionPropertyProperty, TransitionPropertyCollection.None());
        UpdateFromColor(Color);
        RefreshHex();
        UpdatePresentation();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        DetachHexInput();
        _hexTextBox = GetTemplateChild("PART_HexTextBox") as TextBox;
        if (_hexTextBox != null)
        {
            _hexTextBox.AddHandler(KeyDownEvent, new KeyEventHandler(OnHexKeyDown));
            _hexTextBox.LostKeyboardFocus += OnHexLostFocus;
        }

        UpdatePresentation();
    }

    internal override void OnTemplateContentClearing()
    {
        DetachHexInput();
        base.OnTemplateContentClearing();
    }

    private void DetachHexInput()
    {
        if (_hexTextBox == null)
            return;
        _hexTextBox.RemoveHandler(KeyDownEvent, new KeyEventHandler(OnHexKeyDown));
        _hexTextBox.LostKeyboardFocus -= OnHexLostFocus;
        Jalium.UI.Data.BindingOperations.ClearBinding(_hexTextBox, TextBox.TextProperty);
        _hexTextBox = null;
    }

    protected override bool ShouldSuppressAutomaticTransition(DependencyProperty dp) => ReferenceEquals(dp, ColorProperty) && _isUpdatingColor || base.ShouldSuppressAutomaticTransition(dp);
    private void UpdateFromColor(Color color)
    {
        _synchronizingComponents = true;
        try
        {
            var state = ColorPickerColorMath.ToHsv(color, Hue);
            SetCurrentValue(HueProperty, state.Hue);
            SetCurrentValue(SaturationProperty, state.Saturation);
            SetCurrentValue(ValueProperty, state.Value);
            SetCurrentValue(AlphaProperty, (double)color.A);
        }
        finally
        {
            _synchronizingComponents = false;
        }
    }

    private void UpdateColor()
    {
        _isUpdatingColor = true;
        try
        {
            SetCurrentValue(ColorProperty, ColorPickerColorMath.FromHsv(Hue, Saturation, Value, (byte)Alpha));
        }
        finally
        {
            _isUpdatingColor = false;
        }

        RefreshHex();
    }

    /// <summary>Parses #RRGGBB or #AARRGGBB and updates the current color without replacing its binding.</summary>
    public bool TrySetColor(string text)
    {
        if (!ColorPickerColorMath.TryParseHex(text, IsAlphaEnabled ? (byte)255 : Color.A, out var color))
            return false;
        if (!IsAlphaEnabled)
            color = Color.FromArgb(Color.A, color.R, color.G, color.B);
        _hasHexDraft = false;
        SetHexError(false);
        UpdateFromColor(color);
        _isUpdatingColor = true;
        try
        {
            SetCurrentValue(ColorProperty, color);
        }
        finally
        {
            _isUpdatingColor = false;
        }

        RefreshHex();
        return true;
    }

    public bool CommitHexValue()
    {
        if (!_hasHexDraft)
            return true;
        if (TrySetColor(HexText))
            return true;
        SetHexError(true);
        return false;
    }

    public void CancelHexEdit()
    {
        _hasHexDraft = false;
        SetHexError(false);
        RefreshHex();
    }

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsEnabled || e.Handled || !ReferenceEquals(sender, _hexTextBox) || _hexTextBox!.IsImeComposing)
            return;
        if (e.Key == Key.Enter)
        {
            CommitHexValue();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelHexEdit();
            e.Handled = true;
        }
    }

    private void OnHexLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (IsEnabled && ReferenceEquals(sender, _hexTextBox))
            CommitHexValue();
    }

    private void RefreshHex()
    {
        if (_hasHexDraft)
            return;
        _updatingHex = true;
        try
        {
            SetCurrentValue(HexTextProperty, IsAlphaEnabled ? $"#{Color.A:X2}{Color.R:X2}{Color.G:X2}{Color.B:X2}" : $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}");
        }
        finally
        {
            _updatingHex = false;
        }
    }

    private void SetHexError(bool error)
    {
        SetValue(HasHexErrorPropertyKey, error);
        SetValue(HexErrorTextPropertyKey, error ? "Use #RRGGBB or #AARRGGBB" : string.Empty);
        VisualStateManager.GoToState(this, error ? "Invalid" : "Valid", false);
    }

    private void UpdatePresentation()
    {
        SetValue(ShowPreviewPropertyKey, !IsCompact && IsColorPreviewVisible);
        SetValue(ShowHexInputPropertyKey, !IsCompact && IsHexInputVisible);
        SetValue(ShowAlphaSliderPropertyKey, IsAlphaEnabled && IsColorSliderVisible);
        SetValue(EffectiveSpectrumSizePropertyKey, IsCompact ? 120.0 : 200.0);
        SetValue(EffectiveSliderHeightPropertyKey, IsCompact ? 14.0 : 20.0);
        SetValue(EffectiveSpacingPropertyKey, IsCompact ? 4.0 : 8.0);
        VisualStateManager.GoToState(this, IsCompact ? "Compact" : "Expanded", false);
        VisualStateManager.GoToState(this, HasHexError ? "Invalid" : "Valid", false);
    }

    private static object? CoerceHue(DependencyObject d, object? value) => ColorPickerColorMath.Hue((double)value!);
    private static object? CoerceUnit(DependencyObject d, object? value) => ColorPickerColorMath.Unit((double)value!);
    private static object? CoerceAlpha(DependencyObject d, object? value) => double.IsFinite((double)value!) ? Math.Round(Math.Clamp((double)value!, 0, 255)) : 0.0;
    private static void OnComponentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ColorPicker)d;
        if (picker._synchronizingComponents)
            return;
        picker._hasHexDraft = false;
        picker.SetHexError(false);
        picker.UpdateColor();
    }

    private static void OnHexTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ColorPicker)d;
        if (picker._updatingHex)
            return;
        picker._hasHexDraft = true;
        if (picker.HasHexError && ColorPickerColorMath.TryParseHex(picker.HexText, 255, out _))
            picker.SetHexError(false);
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ColorPicker)d;
        if (!picker._isUpdatingColor)
            picker.UpdateFromColor((Color)e.NewValue!);
        picker.RefreshHex();
        picker.RaiseEvent(new ColorChangedEventArgs(ColorChangedEvent, (Color)e.OldValue!, (Color)e.NewValue!));
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ColorPicker)d;
        picker.UpdatePresentation();
        picker.RefreshHex();
        picker.InvalidateMeasure();
    }
}

/// <summary>
/// Specifies the shape of the color spectrum in a ColorPicker.
/// </summary>
public enum ColorSpectrumShape
{
    /// <summary>
    /// Box-shaped spectrum.
    /// </summary>
    Box,
    /// <summary>
    /// Ring-shaped spectrum.
    /// </summary>
    Ring
}

/// <summary>
/// Provides data for the ColorChanged event.
/// </summary>
public sealed class ColorChangedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the old color.
    /// </summary>
    public Color OldColor { get; }
    /// <summary>
    /// Gets the new color.
    /// </summary>
    public Color NewColor { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref = "ColorChangedEventArgs"/> class.
    /// </summary>
    public ColorChangedEventArgs(RoutedEvent routedEvent, Color oldColor, Color newColor)
    {
        RoutedEvent = routedEvent;
        OldColor = oldColor;
        NewColor = newColor;
    }
}
