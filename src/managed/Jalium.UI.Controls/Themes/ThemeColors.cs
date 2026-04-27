using Jalium.UI.Media;

namespace Jalium.UI.Controls.Themes;

/// <summary>
/// Provides theme color definitions for the dark theme.
/// </summary>
public static class ThemeColors
{
    #region Background Colors

    /// <summary>
    /// Default control background color.
    /// </summary>
    public static Color ControlBackground => Color.FromRgb(55, 55, 55);

    /// <summary>
    /// Control background color when mouse is over.
    /// </summary>
    public static Color ControlBackgroundHover => Color.FromRgb(70, 70, 70);

    /// <summary>
    /// Control background color when pressed.
    /// </summary>
    public static Color ControlBackgroundPressed => Color.FromRgb(30, 30, 30);

    /// <summary>
    /// Control background color when disabled.
    /// </summary>
    public static Color ControlBackgroundDisabled => Color.FromRgb(40, 40, 40);

    /// <summary>
    /// Window/page background color.
    /// </summary>
    public static Color WindowBackground => Color.FromRgb(30, 30, 30);

    /// <summary>
    /// Secondary background color for panels and containers.
    /// </summary>
    public static Color SecondaryBackground => Color.FromRgb(45, 45, 45);

    #endregion

    #region Text Colors

    /// <summary>
    /// Primary text color.
    /// </summary>
    public static Color TextPrimary => Color.FromRgb(255, 255, 255);

    /// <summary>
    /// Secondary text color for less important text.
    /// </summary>
    public static Color TextSecondary => Color.FromRgb(180, 180, 180);

    /// <summary>
    /// Disabled text color.
    /// </summary>
    public static Color TextDisabled => Color.FromRgb(90, 90, 90);

    /// <summary>
    /// Placeholder text color (for TextBox, etc.).
    /// </summary>
    public static Color TextPlaceholder => Color.FromRgb(120, 120, 120);

    #endregion

    #region Border Colors

    /// <summary>
    /// Default control border color.
    /// </summary>
    public static Color ControlBorder => Color.FromRgb(80, 80, 80);

    /// <summary>
    /// Control border color when focused.
    /// </summary>
    public static Color ControlBorderFocused => Color.FromRgb(32, 114, 69);

    /// <summary>
    /// Control border color when mouse is over.
    /// </summary>
    public static Color ControlBorderHover => Color.FromRgb(100, 100, 100);

    /// <summary>
    /// Control border color when disabled.
    /// </summary>
    public static Color ControlBorderDisabled => Color.FromRgb(50, 50, 50);

    #endregion

    #region Accent Colors

    /// <summary>
    /// Primary accent color (start stop of the 207245 -> 1C8043 gradient).
    /// </summary>
    public static Color Accent => Color.FromRgb(0x20, 0x72, 0x45);

    /// <summary>
    /// Accent color when mouse is over.
    /// </summary>
    public static Color AccentHover => Color.FromRgb(0x27, 0x8A, 0x52);

    /// <summary>
    /// Accent color when pressed.
    /// </summary>
    public static Color AccentPressed => Color.FromRgb(0x18, 0x5A, 0x37);

    /// <summary>
    /// Accent color when disabled.
    /// </summary>
    public static Color AccentDisabled => Color.FromRgb(0x2A, 0x4A, 0x3A);

    #endregion

    #region Selection Colors

    /// <summary>
    /// Selection background color.
    /// </summary>
    public static Color SelectionBackground => Color.FromArgb(128, 0x1E, 0x79, 0x3F);

    /// <summary>
    /// Selection text color.
    /// </summary>
    public static Color SelectionText => Color.FromRgb(255, 255, 255);

    /// <summary>
    /// Highlight background for list items.
    /// </summary>
    public static Color HighlightBackground => Color.FromRgb(60, 60, 60);

    /// <summary>
    /// Selected item background.
    /// </summary>
    public static Color SelectedItemBackground => Color.FromRgb(0x20, 0x72, 0x45);

    #endregion

    #region Specific Control Colors

    /// <summary>
    /// ScrollBar track background.
    /// </summary>
    public static Color ScrollBarTrack => Color.FromArgb(128, 40, 40, 40);

    /// <summary>
    /// ScrollBar thumb background.
    /// </summary>
    public static Color ScrollBarThumb => Color.FromRgb(80, 80, 80);

    /// <summary>
    /// ScrollBar thumb hover background.
    /// </summary>
    public static Color ScrollBarThumbHover => Color.FromRgb(100, 100, 100);

    /// <summary>
    /// ProgressBar background.
    /// </summary>
    public static Color ProgressBarBackground => Color.FromRgb(50, 50, 50);

    /// <summary>
    /// ProgressBar fill color.
    /// </summary>
    public static Color ProgressBarFill => Color.FromRgb(0x20, 0x72, 0x45);

    /// <summary>
    /// Slider track background.
    /// </summary>
    public static Color SliderTrack => Color.FromRgb(60, 60, 60);

    /// <summary>
    /// Slider thumb background.
    /// </summary>
    public static Color SliderThumb => Color.FromRgb(0x20, 0x72, 0x45);

    /// <summary>
    /// CheckBox/RadioButton check mark color.
    /// </summary>
    public static Color CheckMark => Color.FromRgb(255, 255, 255);

    #endregion

    #region CheckBox/RadioButton Colors

    /// <summary>
    /// CheckBox/RadioButton unchecked background.
    /// </summary>
    public static Color ToggleUncheckedBackground => Color.FromRgb(45, 45, 45);

    /// <summary>
    /// CheckBox/RadioButton unchecked background when hovered.
    /// </summary>
    public static Color ToggleUncheckedBackgroundHover => Color.FromRgb(55, 55, 55);

    /// <summary>
    /// CheckBox/RadioButton unchecked background when pressed.
    /// </summary>
    public static Color ToggleUncheckedBackgroundPressed => Color.FromRgb(35, 35, 35);

    /// <summary>
    /// CheckBox/RadioButton unchecked border.
    /// </summary>
    public static Color ToggleUncheckedBorder => Color.FromRgb(140, 140, 140);

    /// <summary>
    /// CheckBox/RadioButton unchecked border when hovered.
    /// </summary>
    public static Color ToggleUncheckedBorderHover => Color.FromRgb(160, 160, 160);

    /// <summary>
    /// CheckBox/RadioButton checked background (accent).
    /// </summary>
    public static Color ToggleCheckedBackground => Color.FromRgb(0x20, 0x72, 0x45);

    /// <summary>
    /// CheckBox/RadioButton checked background when hovered.
    /// </summary>
    public static Color ToggleCheckedBackgroundHover => Color.FromRgb(0x27, 0x8A, 0x52);

    /// <summary>
    /// CheckBox/RadioButton checked background when pressed.
    /// </summary>
    public static Color ToggleCheckedBackgroundPressed => Color.FromRgb(0x18, 0x5A, 0x37);

    /// <summary>
    /// CheckBox/RadioButton checked border (matches background).
    /// </summary>
    public static Color ToggleCheckedBorder => Color.FromRgb(0x20, 0x72, 0x45);

    /// <summary>
    /// CheckBox/RadioButton checked border when hovered.
    /// </summary>
    public static Color ToggleCheckedBorderHover => Color.FromRgb(0x27, 0x8A, 0x52);

    /// <summary>
    /// CheckBox/RadioButton disabled background.
    /// </summary>
    public static Color ToggleDisabledBackground => Color.FromRgb(40, 40, 40);

    /// <summary>
    /// CheckBox/RadioButton disabled border.
    /// </summary>
    public static Color ToggleDisabledBorder => Color.FromRgb(70, 70, 70);

    /// <summary>
    /// CheckBox/RadioButton disabled check mark.
    /// </summary>
    public static Color ToggleDisabledCheckMark => Color.FromRgb(90, 90, 90);

    /// <summary>
    /// TextBox background color.
    /// </summary>
    public static Color TextBoxBackground => Color.FromRgb(45, 45, 45);

    /// <summary>
    /// ComboBox dropdown background.
    /// </summary>
    public static Color DropdownBackground => Color.FromRgb(50, 50, 50);

    #endregion

    #region TabControl Colors

    /// <summary>
    /// Tab strip background color.
    /// </summary>
    public static Color TabStripBackground => Color.FromRgb(45, 45, 45);

    /// <summary>
    /// Tab strip border color.
    /// </summary>
    public static Color TabStripBorder => Color.FromRgb(60, 60, 60);

    /// <summary>
    /// Tab item background when selected.
    /// </summary>
    public static Color TabItemSelectedBackground => Color.FromRgb(55, 55, 55);

    /// <summary>
    /// Tab item background when hovered.
    /// </summary>
    public static Color TabItemHoverBackground => Color.FromRgb(65, 65, 65);

    /// <summary>
    /// Tab item selection indicator color.
    /// </summary>
    public static Color TabItemIndicator => Color.FromRgb(0x20, 0x72, 0x45);

    /// <summary>
    /// Tab content background color.
    /// </summary>
    public static Color TabContentBackground => Color.FromRgb(30, 30, 30);

    #endregion

    #region DockLayout Colors

    public static Color DockTabStripBackground => Color.FromRgb(37, 37, 37);

    public static Color DockTabStripBorder => Color.FromRgb(60, 60, 60);

    public static Color DockTabItemSelectedBackground => Color.FromRgb(30, 30, 30);

    public static Color DockTabItemHoverBackground => Color.FromRgb(45, 45, 45);

    public static Color DockContentBackground => Color.FromRgb(30, 30, 30);

    public static Color DockSplitterBackground => Color.FromRgb(37, 37, 37);

    public static Color DockSplitterHover => Color.FromRgb(60, 60, 60);

    public static Color DockCloseButtonHover => Color.FromRgb(232, 17, 35);

    #endregion

    #region IME / Composition Colors

    /// <summary>
    /// IME composition background color.
    /// </summary>
    public static Color CompositionBackground => Color.FromRgb(60, 60, 80);

    /// <summary>
    /// IME composition text color.
    /// </summary>
    public static Color CompositionText => Color.FromRgb(255, 255, 200);

    /// <summary>
    /// IME composition underline color.
    /// </summary>
    public static Color CompositionUnderline => Color.FromRgb(200, 200, 100);

    #endregion

    #region Dropdown Colors

    /// <summary>
    /// Dropdown shadow color.
    /// </summary>
    public static Color DropdownShadow => Color.FromArgb(40, 0, 0, 0);

    #endregion

    #region Title Bar Colors

    /// <summary>
    /// Title bar background color.
    /// </summary>
    public static Color TitleBarBackground => Color.FromRgb(32, 32, 32);

    /// <summary>
    /// Title bar text color.
    /// </summary>
    public static Color TitleBarText => Color.FromRgb(255, 255, 255);

    /// <summary>
    /// Title bar button background (transparent by default).
    /// </summary>
    public static Color TitleBarButtonBackground => Color.Transparent;

    /// <summary>
    /// Title bar button hover background.
    /// </summary>
    public static Color TitleBarButtonHover => Color.FromRgb(60, 60, 60);

    /// <summary>
    /// Title bar button pressed background.
    /// </summary>
    public static Color TitleBarButtonPressed => Color.FromRgb(45, 45, 45);

    /// <summary>
    /// Title bar close button hover background (red).
    /// </summary>
    public static Color TitleBarCloseButtonHover => Color.FromRgb(232, 17, 35);

    /// <summary>
    /// Title bar close button pressed background.
    /// </summary>
    public static Color TitleBarCloseButtonPressed => Color.FromRgb(200, 15, 30);

    /// <summary>
    /// Title bar button glyph color.
    /// </summary>
    public static Color TitleBarGlyph => Color.FromRgb(255, 255, 255);

    #endregion
}
