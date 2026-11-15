using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class FontDialogTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void DefaultTemplateUsesAReadablePreviewAndBindableSelection()
    {
        var target = new FontDialog { FontSize = 18, Color = Color.Black };
        var window = Create(target);
        var preview = Assert.IsType<TextBlock>(window.FindName("PART_Preview"));
        Assert.Equal(18, preview.FontSize);
        Assert.Equal(Color.Black, Assert.IsType<SolidColorBrush>(preview.Foreground).Color);
        Assert.Equal(Color.White, Assert.IsType<SolidColorBrush>(window.PreviewBackground).Color);
        var editor = Assert.IsType<TextBox>(window.FindName("PART_FontSize"));
        editor.SetCurrentValue(TextBox.TextProperty, "24");
        Assert.Equal(24, window.SelectedFontSize);
        Assert.Equal(24, preview.FontSize);
        Assert.Equal(18, target.FontSize);
        window.SelectedColor = Color.White;
        Assert.Equal(Color.Black, Assert.IsType<SolidColorBrush>(window.PreviewBackground).Color);
        Assert.Equal(nint.Zero, window.Handle);
        Cancel(window);
        Assert.Equal(18, target.FontSize);
        Assert.Equal(Color.Black, target.Color);
    }

    [Fact]
    public void TabFromTheFontFamilyEditorReachesTheSizeField()
    {
        var window = Create();
        var family = Assert.IsType<ComboBox>(window.FindName("PART_FontFamily"));
        var editor = Assert.IsType<TextBox>(family.FindName("PART_EditableTextBox"));
        var arrow = Assert.IsType<ToggleButton>(family.FindName("PART_ToggleButton"));
        var size = Assert.IsType<TextBox>(window.FindName("PART_FontSize"));
        Assert.False(arrow.Focusable);
        Assert.False(arrow.IsTabStop);
        Assert.Same(size, editor.PredictFocus(FocusNavigationDirection.Next));
        Cancel(window);
    }

    [Theory]
    [InlineData("not a size")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("0")]
    [InlineData("501")]
    public void InvalidSizesDoNotCommitOrEnableTheDefaultButton(string text)
    {
        var window = Create();
        window.FontSizeText = text;
        Assert.False(window.CanAccept);
        Assert.False(Assert.IsType<Button>(window.FindName("PART_AcceptButton")).IsEnabled);
        Assert.NotEmpty(window.ValidationMessage);
        Assert.Equal("Invalid", VisualStateManager.GetCurrentStateName(window, "ValidationStates"));
        window.FontSizeText = "20";
        Assert.True(window.CanAccept);
        Assert.Equal("Valid", VisualStateManager.GetCurrentStateName(window, "ValidationStates"));
        Cancel(window);
    }

    [Theory]
    [InlineData("#not-a-color")]
    [InlineData("#GGGGGG")]
    public void InvalidColorAndHiddenOptionsUseIndependentVisualStates(string color)
    {
        var window = Create();
        window.ColorText = color;
        Assert.False(window.CanAccept);
        window.ShowColor = false;
        window.ShowEffects = false;
        Assert.True(window.CanAccept);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<StackPanel>(window.FindName("PART_ColorPanel")).Visibility);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<WrapPanel>(window.FindName("PART_EffectsPanel")).Visibility);
        window.ShowColor = true;
        Assert.False(window.CanAccept);
        window.ColorText = "#204080";
        Assert.True(window.CanAccept);
        Assert.Equal(Color.FromRgb(32, 64, 128), window.SelectedColor);
        Cancel(window);
    }

    [Fact]
    public void SelectionEditsPreserveBindingsAndOtherDecorations()
    {
        var model = new SizeModel { Size = 14 };
        var window = Create(new FontDialog
        {
            TextDecorations = new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.OverLine } }
        });
        window.SetBinding(FontDialogWindow.SelectedFontSizeProperty,
            new Binding(nameof(SizeModel.Size)) { Source = model, Mode = BindingMode.TwoWay });
        Assert.IsType<TextBox>(window.FindName("PART_FontSize")).SetCurrentValue(TextBox.TextProperty, "22");
        Assert.Equal(22, model.Size);
        Assert.NotNull(window.GetBindingExpression(FontDialogWindow.SelectedFontSizeProperty));
        window.IsUnderline = true;
        window.IsStrikeout = true;
        Assert.Contains(window.SelectedTextDecorations!, decoration => decoration.Location == TextDecorationLocation.OverLine);
        Assert.Contains(window.SelectedTextDecorations!, decoration => decoration.Location == TextDecorationLocation.Underline);
        Assert.Contains(window.SelectedTextDecorations!, decoration => decoration.Location == TextDecorationLocation.Strikethrough);
        Cancel(window);
    }

    [Fact]
    public void ReplacingTheTemplatePreservesPendingEditsAndDisconnectsOldEditorsAndButtons()
    {
        var target = new FontDialog { FontSize = 12 };
        var window = Create(target);
        window.FontSizeText = "26";
        var oldSize = Assert.IsType<TextBox>(window.FindName("PART_FontSize"));
        var oldAccept = Assert.IsAssignableFrom<ButtonBase>(window.FindName("PART_AcceptButton"));
        var original = window.Template;
        window.Template = null;
        oldSize.Text = "40";
        oldAccept.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, oldAccept));
        Assert.Equal(26, window.SelectedFontSize);
        Assert.Equal(12, target.FontSize);
        window.Template = original;
        Layout(window);
        Assert.Equal("26", Assert.IsType<TextBox>(window.FindName("PART_FontSize")).Text);
        Click(window, "PART_AcceptButton");
        Assert.Equal(26, target.FontSize);
        Assert.True(window.DialogResult);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AClosingVetoKeepsTheTargetUnchangedUntilTheAcceptedCloseActuallyCompletes(bool cancelButton)
    {
        var target = new FontDialog { FontSize = 12 };
        var window = Create(target);
        window.FontSizeText = "28";
        var veto = true;
        window.Closing += (_, e) => e.Cancel = veto;
        Click(window, "PART_AcceptButton");
        Assert.Equal(12, target.FontSize);
        veto = false;
        if (cancelButton) Click(window, "PART_CancelButton");
        else window.Close();
        Assert.Equal(12, target.FontSize);
        Assert.NotEqual(true, window.DialogResult);
    }

    private static FontDialogWindow Create(FontDialog? target = null)
    {
        var window = new FontDialogWindow(target ?? new FontDialog());
        System.ComponentModel.DesignerProperties.SetIsInDesignMode(window, true);
        window.Resources.MergedDictionaries.Add(Defaults.Value);
        window.UpdateDefaultStyle();
        window.ApplyTemplate();
        Layout(window);
        return window;
    }
    private static void Layout(FontDialogWindow window)
    {
        window.Measure(new Size(520, 800));
        window.Arrange(new Rect(0, 0, 520, window.DesiredSize.Height));
        window.UpdateLayout();
    }
    private static void Click(FontDialogWindow window, string name)
    {
        var button = Assert.IsAssignableFrom<ButtonBase>(window.FindName(name));
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }
    private static void Cancel(FontDialogWindow window) => Click(window, "PART_CancelButton");
    private sealed class SizeModel : INotifyPropertyChanged
    {
        private double _size;
        public double Size { get => _size; set { _size = value; PropertyChanged?.Invoke(this, new(nameof(Size))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
