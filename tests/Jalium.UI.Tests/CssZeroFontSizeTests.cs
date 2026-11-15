using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Charts;
using Jalium.UI.Controls.Editor;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.TextEffects;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssZeroFontSizeTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("0px")]
    [InlineData("0%")]
    [InlineData("0em")]
    [InlineData("calc(1px - 2px)")]
    public void ZeroAndOutOfRangeMathAreValidFontSizes(string value)
    {
        Assert.True(Css.Supports("font-size", value));
        var text = new TextBlock();
        Css.SetStyle(text, "font-size:" + value);
        Assert.Equal(0, text.FontSize);
    }

    [Fact]
    public void FontShorthandAcceptsZeroSize()
    {
        Assert.True(Css.Supports("font", "0 serif"));
        var text = new TextBlock();
        Css.SetStyle(text, "font:0 serif");
        Assert.Equal(0, text.FontSize);
    }

    [Theory]
    [InlineData("-1px")]
    [InlineData("-1%")]
    [InlineData("-1em")]
    public void NegativeLiteralFontSizesRemainInvalid(string value)
        => Assert.False(Css.Supports("font-size", value));

    [Fact]
    public void ZeroFontSizeInheritsAndPreservesZeroEmBasis()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyle(root, "font-size:0px");
        Css.SetStyle(child, "width:calc(2em + 3px);height:3px");
        root.Measure(new Size(100, 100));
        root.Arrange(new Rect(0, 0, 100, 100));

        Assert.Equal(0, (double)root.GetValue(TextElement.FontSizeProperty)!);
        Assert.Equal(0, (double)child.GetValue(TextElement.FontSizeProperty)!);
        Assert.Equal(3, child.ActualWidth);
    }

    [Fact]
    public void RelativeMathClampsAtZeroAndRecomputesAgainstParent()
    {
        var child = new TextBlock();
        var root = new Border { Child = child };
        Css.SetStyle(root, "font-size:20px");
        Css.SetStyle(child, "font-size:calc(1em - 30px)");
        root.Measure(new Size(100, 100));
        root.Arrange(new Rect(0, 0, 100, 100));
        Assert.Equal(0, child.FontSize);

        Css.SetStyle(root, "font-size:40px");
        root.UpdateLayout();
        Assert.Equal(10, child.FontSize);
    }

    [Fact]
    public void ZeroFontSizeHasZeroMetricsAndDrawsNoText()
    {
        var text = new TextBlock { Text = "M", Foreground = Brushes.Black };
        Css.SetStyle(text, "font-size:0");
        text.Measure(new Size(100, 100));
        Assert.Equal(0, text.DesiredSize.Width);
        Assert.Equal(0, text.DesiredSize.Height);
        text.Arrange(new Rect(0, 0, 100, 100));

        var drawing = new TextDrawingContext();
        text.Render(drawing);
        Assert.Empty(drawing.Texts);
        var formatted = new FormattedText("M", "Arial", 0);
        Assert.False(TextMeasurement.MeasureText(formatted));
        Assert.Equal(0, formatted.Width);
        Assert.Equal(0, TextMeasurement.GetFontMetrics("Arial", 0).LineHeight);
        Assert.Equal(0, TextMeasurement.GetFontUnitMetrics("Arial", 0).ZeroAdvance);

        Css.SetStyle(text, "font-size:20px");
        text.Measure(new Size(100, 100));
        text.Arrange(new Rect(0, 0, 100, 100));
        text.Render(drawing);
        Assert.NotEmpty(drawing.Texts);
    }

    [Fact]
    public void ZeroSizedRunDoesNotHideItsVisibleSibling()
    {
        var zero = new Run("A");
        var block = new TextBlock(zero) { Foreground = Brushes.Black };
        block.Inlines.Add(new Run("B"));
        Css.SetStyle(block, "font-size:20px");
        Css.SetStyle(zero, "font-size:0");
        block.Measure(new Size(100, 100));
        block.Arrange(new Rect(0, 0, 100, 100));
        var drawing = new TextDrawingContext();
        block.Render(drawing);

        Assert.Single(drawing.Texts);
        Assert.Equal("B", drawing.Texts[0].Text);
        Assert.Equal(20, drawing.Texts[0].FontSize);

        Css.SetStyle(block, "font-size:0");
        Css.SetStyle(zero, "font-size:20px");
        block.Measure(new Size(100, 100));
        block.Arrange(new Rect(0, 0, 100, 100));
        drawing.Texts.Clear();
        block.Render(drawing);
        Assert.Single(drawing.Texts);
        Assert.Equal("A", drawing.Texts[0].Text);
    }

    [Fact]
    public void TextBoxZeroFontSizeDoesNotDrawGlyphs()
    {
        var box = new TextBox { Text = "abc", Foreground = Brushes.Black };
        Css.SetStyle(box, "font-size:0");
        box.Measure(new Size(120, 40));
        box.Arrange(new Rect(0, 0, 120, 40));
        var drawing = new TextDrawingContext();
        box.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(box, "font-size:16px");
        box.Measure(new Size(120, 40));
        box.Arrange(new Rect(0, 0, 120, 40));
        box.Render(drawing);
        Assert.NotEmpty(drawing.Texts);
    }

    [Fact]
    public void RichTextBoxDocumentZeroFontSizeDoesNotDrawGlyphs()
    {
        var document = new FlowDocument(new Paragraph(new Run("abc")));
        var box = new RichTextBox(document) { Foreground = Brushes.Black };
        Css.SetStyle(document, "font-size:0");
        box.Measure(new Size(120, 40));
        box.Arrange(new Rect(0, 0, 120, 40));
        var drawing = new TextDrawingContext();
        box.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(document, "font-size:16px");
        box.Measure(new Size(120, 40));
        box.Arrange(new Rect(0, 0, 120, 40));
        box.Render(drawing);
        Assert.NotEmpty(drawing.Texts);
    }

    [Theory]
    [InlineData("auto-complete")]
    [InlineData("number")]
    [InlineData("password")]
    public void SelfRenderedEditorsRespectZeroFontSize(string kind)
    {
        Control box = kind switch
        {
            "auto-complete" => new AutoCompleteBox { Text = "abc" },
            "number" => new NumberBox { Value = 123 },
            _ => new PasswordBox { Password = "abc" },
        };
        Css.SetStyle(box, "font-size:0");
        box.Measure(new Size(140, 48));
        box.Arrange(new Rect(0, 0, 140, 48));
        var drawing = new TextDrawingContext();
        box.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(box, "font-size:16px");
        box.Measure(new Size(140, 48));
        box.Arrange(new Rect(0, 0, 140, 48));
        box.Render(drawing);
        Assert.NotEmpty(drawing.Texts);
    }

    [Fact]
    public void AutoCompleteSuggestionsUseTheComputedZeroSize()
    {
        var box = new AutoCompleteBox { Text = "a" };
        box.FilteredItems.Add("apple");
        box.IsDropDownOpen = true;
        Css.SetStyle(box, "font-size:0");
        box.Measure(new Size(140, 100));
        box.Arrange(new Rect(0, 0, 140, 100));
        var drawing = new TextDrawingContext();
        box.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(box, "font-size:16px");
        box.Measure(new Size(140, 100));
        box.Arrange(new Rect(0, 0, 140, 100));
        box.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text == "apple");
    }

    [Fact]
    public void NumberBoxHeaderUsesTheComputedZeroSize()
    {
        var box = new NumberBox { Header = "Price", Value = 123 };
        Css.SetStyle(box, "font-size:0");
        box.Measure(new Size(140, 70));
        box.Arrange(new Rect(0, 0, 140, 70));
        var drawing = new TextDrawingContext();
        box.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(box, "font-size:16px");
        box.Measure(new Size(140, 70));
        box.Arrange(new Rect(0, 0, 140, 70));
        box.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text == "Price");
    }

    [Fact]
    public void ZeroSizedEditorHitTestingAvoidsZeroLineHeightDivision()
    {
        var box = new HitTestAutoCompleteBox { Text = "abc" };
        Css.SetStyle(box, "font-size:0");
        box.Measure(new Size(140, 48));
        box.Arrange(new Rect(0, 0, 140, 48));
        Assert.Equal(0, box.HitCaret(new Point(60, 20)));
    }

    [Fact]
    public void LabelMeasuresAndDrawsAtComputedZeroSize()
    {
        var label = new Label { Content = "abc", Foreground = Brushes.Black };
        Css.SetStyle(label, "font-size:0");
        label.Measure(new Size(140, 40));
        Assert.Equal(0, label.DesiredSize.Width);
        Assert.Equal(0, label.DesiredSize.Height);
        label.Arrange(new Rect(0, 0, 140, 40));
        var drawing = new TextDrawingContext();
        label.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(label, "font-size:16px");
        label.Measure(new Size(140, 40));
        Assert.True(label.DesiredSize.Width > 0);
        label.Arrange(new Rect(0, 0, 140, 40));
        label.Render(drawing);
        Assert.NotEmpty(drawing.Texts);
    }

    [Fact]
    public void AccessTextOmitsMnemonicUnderlineAtZeroSize()
    {
        var accessText = new AccessText { Text = "_Save", Foreground = Brushes.Black };
        Css.SetStyle(accessText, "font-size:0");
        accessText.Measure(new Size(140, 40));
        accessText.Arrange(new Rect(0, 0, 140, 40));
        var drawing = new TextDrawingContext();
        accessText.Render(drawing);
        Assert.Empty(drawing.Texts);
        Assert.Empty(drawing.Lines);

        Css.SetStyle(accessText, "font-size:16px");
        accessText.Measure(new Size(140, 40));
        accessText.Arrange(new Rect(0, 0, 140, 40));
        accessText.Render(drawing);
        Assert.NotEmpty(drawing.Texts);
        Assert.NotEmpty(drawing.Lines);
    }

    [Fact]
    public void HyperlinkButtonMeasuresAndDrawsAtComputedZeroSize()
    {
        var button = new HyperlinkButton { Content = "Link", Foreground = Brushes.Black };
        Css.SetStyle(button, "font-size:0");
        Assert.Equal(0, button.FontSize);
        button.Measure(new Size(140, 40));
        Assert.Equal(0, button.DesiredSize.Width);
        Assert.Equal(0, button.DesiredSize.Height);
        button.Arrange(new Rect(0, 0, 140, 40));
        var drawing = new TextDrawingContext();
        button.Render(drawing);
        Assert.Empty(drawing.Texts);
        Assert.Empty(drawing.Lines);

        Css.SetStyle(button, "font-size:16px");
        button.Measure(new Size(140, 40));
        button.Arrange(new Rect(0, 0, 140, 40));
        button.Render(drawing);
        Assert.NotEmpty(drawing.Texts);
        Assert.NotEmpty(drawing.Lines);
    }

    [Fact]
    public void FontIconMeasuresAndDrawsAtComputedZeroSize()
    {
        var icon = new FontIcon { Glyph = "A" };
        Css.SetStyle(icon, "font-size:0");
        Assert.Equal(0, icon.FontSize);
        icon.Measure(new Size(40, 40));
        Assert.Equal(0, icon.DesiredSize.Width);
        Assert.Equal(0, icon.DesiredSize.Height);
        icon.Arrange(new Rect(0, 0, 40, 40));
        var drawing = new TextDrawingContext();
        icon.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(icon, "font-size:16px");
        icon.Measure(new Size(40, 40));
        icon.Arrange(new Rect(0, 0, 40, 40));
        icon.Render(drawing);
        Assert.NotEmpty(drawing.Texts);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void DateAndTimePickersUseZeroSizeForHeaderAndDisplayText(bool datePicker, bool selected)
    {
        Control picker = datePicker
            ? new DatePicker
            {
                Header = "When",
                Foreground = Brushes.Black,
                SelectedDate = selected ? new DateTime(2026, 9, 27) : null,
            }
            : new TimePicker
            {
                Header = "When",
                Foreground = Brushes.Black,
                SelectedTime = selected ? new TimeSpan(9, 30, 0) : null,
            };
        Css.SetStyle(picker, "font-size:0");
        Assert.Equal(0, picker.FontSize);
        picker.Measure(new Size(160, 80));
        Assert.Equal(36, picker.DesiredSize.Height);
        picker.Arrange(new Rect(0, 0, 160, 80));
        var drawing = new TextDrawingContext();
        picker.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(picker, "font-size:16px");
        picker.Measure(new Size(160, 80));
        Assert.True(picker.DesiredSize.Height > 36);
        picker.Arrange(new Rect(0, 0, 160, 80));
        picker.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text == "When");
        Assert.Contains(drawing.Texts, text => text.Text != "When");
    }

    [Fact]
    public void ColorPickerHexLabelUsesZeroFontSize()
    {
        var picker = new ColorPicker
        {
            IsColorSpectrumVisible = false,
            IsColorSliderVisible = false,
            IsColorPreviewVisible = true,
            IsHexInputVisible = true,
        };
        Css.SetStyle(picker, "font-size:0");
        picker.Measure(new Size(240, 80));
        picker.Arrange(new Rect(0, 0, 240, 80));
        var drawing = new TextDrawingContext();
        picker.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(picker, "font-size:16px");
        picker.Measure(new Size(240, 80));
        picker.Arrange(new Rect(0, 0, 240, 80));
        picker.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text.StartsWith('#'));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MessageControlsSuppressTitleAndBodyAtZeroFontSize(bool toast)
    {
        Control banner = toast
            ? new ToastNotificationItem
            {
                Title = "Notice",
                Message = "Body",
                IsAutoDismissEnabled = false,
            }
            : new InfoBar { Title = "Notice", Message = "Body" };
        Css.SetStyle(banner, "font-size:0");
        banner.Measure(new Size(220, 100));
        var zeroHeight = banner.DesiredSize.Height;
        banner.Arrange(new Rect(0, 0, 220, 100));
        var drawing = new TextDrawingContext();
        banner.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(banner, "font-size:16px");
        banner.Measure(new Size(220, 100));
        Assert.True(banner.DesiredSize.Height > zeroHeight);
        banner.Arrange(new Rect(0, 0, 220, 100));
        banner.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text == "Notice");
        Assert.Contains(drawing.Texts, text => text.Text == "Body");
    }

    [Theory]
    [InlineData(CalendarMode.Month)]
    [InlineData(CalendarMode.Year)]
    [InlineData(CalendarMode.Decade)]
    public void CalendarViewsSuppressLabelsAtZeroFontSize(CalendarMode mode)
    {
        var calendar = new Calendar { DisplayMode = mode, DisplayDate = new DateTime(2026, 9, 27) };
        Css.SetStyle(calendar, "font-size:0");
        calendar.Measure(new Size(300, 300));
        calendar.Arrange(new Rect(0, 0, 300, 300));
        var drawing = new TextDrawingContext();
        calendar.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(calendar, "font-size:16px");
        calendar.Measure(new Size(300, 300));
        calendar.Arrange(new Rect(0, 0, 300, 300));
        calendar.Render(drawing);
        Assert.NotEmpty(drawing.Texts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CalendarButtonsSuppressLabelsAtZeroFontSize(bool dayButton)
    {
        Control button = dayButton
            ? new CalendarDayButton { Content = "27", IsBlackedOut = true }
            : new CalendarButton { Content = "September" };
        Css.SetStyle(button, "font-size:0");
        button.Measure(new Size(100, 40));
        button.Arrange(new Rect(0, 0, 100, 40));
        var drawing = new TextDrawingContext();
        button.Render(drawing);
        Assert.Empty(drawing.Texts);
        if (dayButton) Assert.Empty(drawing.Lines);

        Css.SetStyle(button, "font-size:16px");
        button.Measure(new Size(100, 40));
        button.Arrange(new Rect(0, 0, 100, 40));
        button.Render(drawing);
        Assert.NotEmpty(drawing.Texts);
        if (dayButton) Assert.NotEmpty(drawing.Lines);
    }

    [Fact]
    public void CalendarWeekdayLabelsScaleWithComputedFontSize()
    {
        var calendar = new Calendar { DisplayDate = new DateTime(2026, 9, 27) };
        calendar.Measure(new Size(300, 300));
        calendar.Arrange(new Rect(0, 0, 300, 300));
        var drawing = new TextDrawingContext();
        calendar.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text == "Su" && text.FontSize == 11);

        drawing.Texts.Clear();
        Css.SetStyle(calendar, "font-size:28px");
        calendar.Measure(new Size(300, 300));
        calendar.Arrange(new Rect(0, 0, 300, 300));
        calendar.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text == "Su" && text.FontSize == 22);
    }

    [Theory]
    [InlineData("legacy-menu")]
    [InlineData("menu-bar")]
    [InlineData("menu-flyout")]
    [InlineData("status")]
    [InlineData("tab")]
    public void NavigationItemsSuppressComputedZeroSizeGlyphs(string kind)
    {
        const string label = "Open a very long document";
        Control item = kind switch
        {
            "legacy-menu" => new MenuItem { Header = label, Icon = "X", InputGestureText = "Ctrl+O" },
            "menu-bar" => new MenuBarItem { Title = label },
            "menu-flyout" => new MenuFlyoutItem
            {
                Text = label, Icon = "X", KeyboardAcceleratorTextOverride = "Ctrl+O",
            },
            "status" => new StatusBarItem { Content = label },
            _ => new TabItem { Header = label },
        };
        Css.SetStyle(item, "font-size:0");
        Assert.Equal(0, item.FontSize);
        item.Measure(new Size(240, 50));
        var zeroWidth = item.DesiredSize.Width;
        item.Arrange(new Rect(0, 0, 240, 50));
        var drawing = new TextDrawingContext();
        item.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(item, "font-size:16px");
        item.Measure(new Size(240, 50));
        Assert.True(item.DesiredSize.Width > zeroWidth);
        item.Arrange(new Rect(0, 0, 240, 50));
        item.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text == label);
        if (kind is "legacy-menu" or "menu-flyout")
            Assert.Contains(drawing.Texts, text => text.Text == "Ctrl+O");
    }

    [Fact]
    public void TopLevelMenuItemSuppressesZeroSizedHeader()
    {
        var item = new MenuItem { Header = "File" };
        var menu = new Menu();
        menu.Items.Add(item);
        Css.SetStyle(item, "font-size:0");
        menu.Measure(new Size(180, 40));
        menu.Arrange(new Rect(0, 0, 180, 40));
        var drawing = new TextDrawingContext();
        item.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(item, "font-size:16px");
        menu.Measure(new Size(180, 40));
        menu.Arrange(new Rect(0, 0, 180, 40));
        item.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text == "File");
    }

    [Fact]
    public void FlyoutIconAndAcceleratorScaleWithMenuFontSize()
    {
        var item = new MenuFlyoutItem
        {
            Text = "Open", Icon = "X", KeyboardAcceleratorTextOverride = "Ctrl+O",
        };
        Css.SetStyle(item, "font-size:28px");
        item.Measure(new Size(280, 40));
        item.Arrange(new Rect(0, 0, 280, 40));
        var drawing = new TextDrawingContext();
        item.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text == "X" && text.FontSize == 28);
        Assert.Contains(drawing.Texts, text => text.Text == "Open" && text.FontSize == 28);
        Assert.Contains(drawing.Texts, text => text.Text == "Ctrl+O" && text.FontSize == 24);
    }

    [Fact]
    public void TextEffectPresenterUsesZeroSizedCellMetricsAndPaint()
    {
        var presenter = new TextEffectPresenter
        {
            TextEffect = null,
            Text = "abc",
            Foreground = Brushes.Black,
        };
        Css.SetStyle(presenter, "font-size:0");
        Assert.Equal(0, presenter.FontSize);
        presenter.Measure(new Size(100, 100));
        Assert.Equal(0, presenter.DesiredSize.Width);
        Assert.Equal(0, presenter.DesiredSize.Height);
        presenter.Arrange(new Rect(0, 0, 100, 100));
        var drawing = new TextDrawingContext();
        presenter.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(presenter, "font-size:16px");
        presenter.Measure(new Size(100, 100));
        Assert.True(presenter.DesiredSize.Width > 0);
        presenter.Arrange(new Rect(0, 0, 100, 100));
        presenter.Render(drawing);
        Assert.NotEmpty(drawing.Texts);
    }

    [Fact]
    public void TextEffectPresenterHidesExitingCellsWhenFontSizeBecomesZero()
    {
        var presenter = new TextEffectPresenter { Text = "old", Foreground = Brushes.Black };
        presenter.Measure(new Size(100, 100));
        presenter.Arrange(new Rect(0, 0, 100, 100));
        presenter.AdvanceFrameForTesting(1000);
        presenter.Text = "new";
        presenter.AdvanceFrameForTesting(30);

        Css.SetStyle(presenter, "font-size:0");
        presenter.Measure(new Size(100, 100));
        presenter.Arrange(new Rect(0, 0, 100, 100));
        var drawing = new TextDrawingContext();
        presenter.Render(drawing);
        Assert.Empty(drawing.Texts);
    }

    [Theory]
    [InlineData(DiffViewMode.SideBySide)]
    [InlineData(DiffViewMode.Unified)]
    public void DiffViewerSuppressesZeroSizedLineNumbersAndDiffText(DiffViewMode mode)
    {
        var viewer = new DiffViewer
        {
            OriginalText = "hello world",
            ModifiedText = "hello there",
            ViewMode = mode,
            ShowMinimap = false,
        };
        Css.SetStyle(viewer, "font-size:0");
        viewer.Measure(new Size(300, 120));
        viewer.Arrange(new Rect(0, 0, 300, 120));
        var drawing = new TextDrawingContext();
        viewer.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(viewer, "font-size:16px");
        viewer.Measure(new Size(300, 120));
        viewer.Arrange(new Rect(0, 0, 300, 120));
        viewer.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text.Contains("hello"));
    }

    [Fact]
    public void HexEditorSuppressesZeroSizedColumnsAndInterpretation()
    {
        var editor = new HexEditor
        {
            Data = [0x41, 0x42, 0x43, 0x44],
            ShowDataInterpretation = true,
        };
        Css.SetStyle(editor, "font-size:0");
        editor.Measure(new Size(500, 120));
        Assert.Equal(2, editor.DesiredSize.Height);
        editor.Arrange(new Rect(0, 0, 500, 120));
        var drawing = new TextDrawingContext();
        editor.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(editor, "font-size:16px");
        editor.Measure(new Size(500, 120));
        Assert.True(editor.DesiredSize.Height > 2);
        editor.Arrange(new Rect(0, 0, 500, 120));
        editor.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text.Contains("41"));
        Assert.Contains(drawing.Texts, text => text.Text.Contains("ABCD"));
    }

    [Fact]
    public void TerminalUsesZeroSizedCellMetricsAndSuppressesGlyphs()
    {
        var terminal = new Terminal();
        terminal.Write("abc");
        Css.SetStyle(terminal, "font-size:0");
        terminal.EnsureCellMetricsPublic();
        Assert.Equal(0, terminal.CellWidth);
        Assert.Equal(0, terminal.CellHeight);
        var drawing = new TextDrawingContext();
        terminal.RenderView(drawing, new Size(200, 100));
        Assert.Empty(drawing.Texts);

        Css.SetStyle(terminal, "font-size:16px");
        terminal.EnsureCellMetricsPublic();
        Assert.True(terminal.CellWidth > 0);
        Assert.True(terminal.CellHeight > 0);
        terminal.RenderView(drawing, new Size(200, 100));
        Assert.Contains(drawing.Texts, text => text.Text.Contains("abc"));
    }

    [Fact]
    public void EditControlSuppressesZeroSizedDocumentAndRestoresGlyphs()
    {
        var editor = new EditControl { Text = "abc", ShowMinimap = false };
        Css.SetStyle(editor, "font-size:0");
        editor.Measure(new Size(200, 100));
        editor.Arrange(new Rect(0, 0, 200, 100));
        editor.UpdateScrollBarsForTesting(new Size(200, 100));
        var view = (EditorView)typeof(EditControl)
            .GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(editor)!;
        Assert.Equal(0, view.LineHeight);
        Assert.Equal(0, view.CharWidth);
        Assert.Equal(0, view.GutterWidth);
        Assert.Equal(0, view.TotalContentHeight);
        Assert.False(editor.IsVerticalScrollBarVisibleForTesting);
        Assert.False(editor.IsHorizontalScrollBarVisibleForTesting);
        var drawing = new TextDrawingContext();
        editor.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(editor, "font-size:16px");
        editor.Measure(new Size(200, 100));
        editor.Arrange(new Rect(0, 0, 200, 100));
        editor.UpdateScrollBarsForTesting(new Size(200, 100));
        Assert.True(view.LineHeight > 0);
        Assert.True(view.CharWidth > 0);
        editor.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text.Contains("abc"));
    }

    [Fact]
    public void EditControlRecomputesHorizontalExtentWhenFontSizeChanges()
    {
        var editor = new EditControl
        {
            Text = new string('x', 24),
            ShowLineNumbers = false,
            ShowMinimap = false,
        };
        editor.Measure(new Size(180, 100));
        editor.Arrange(new Rect(0, 0, 180, 100));

        Css.SetStyle(editor, "font-size:16px");
        editor.UpdateScrollBarsForTesting(new Size(180, 100));
        Assert.True(editor.IsHorizontalScrollBarVisibleForTesting);

        Css.SetStyle(editor, "font-size:0");
        editor.UpdateScrollBarsForTesting(new Size(180, 100));
        Assert.False(editor.IsHorizontalScrollBarVisibleForTesting);

        Css.SetStyle(editor, "font-size:8px");
        editor.UpdateScrollBarsForTesting(new Size(180, 100));
        Assert.False(editor.IsHorizontalScrollBarVisibleForTesting);
    }

    [Fact]
    public void EditControlClampsScrollOffsetWhenFontSizeBecomesZero()
    {
        var editor = new EditControl
        {
            Text = string.Join("\n", Enumerable.Range(1, 80).Select(line => $"line {line}")),
            ShowMinimap = false,
            IsScrollInertiaEnabled = false,
        };
        Css.SetStyle(editor, "font-size:16px");
        editor.Measure(new Size(180, 100));
        editor.Arrange(new Rect(0, 0, 180, 100));
        editor.UpdateScrollBarsForTesting(new Size(180, 100));
        editor.ScrollToLine(60);
        Assert.True(editor.VerticalOffsetForTesting > 0);

        Css.SetStyle(editor, "font-size:0");
        editor.UpdateScrollBarsForTesting(new Size(180, 100));
        Assert.Equal(0, editor.VerticalOffsetForTesting);
        Assert.False(editor.IsVerticalScrollBarVisibleForTesting);
    }

    [Fact]
    public void ChartLegendKeepsMarkersWithoutZeroSizedLabels()
    {
        var legend = new ChartLegend
        {
            Items = new[] { new ChartLegendItem { Label = "Revenue", Brush = Brushes.Black } },
        };
        Css.SetStyle(legend, "font-size:0");
        legend.Measure(new Size(200, 100));
        double zeroWidth = legend.DesiredSize.Width;
        legend.Arrange(new Rect(0, 0, 200, 100));
        var drawing = new TextDrawingContext();
        legend.Render(drawing);
        Assert.Empty(drawing.Texts);
        Assert.Contains(drawing.Rectangles, rect => rect.Width == 12 && rect.Height == 12);

        Css.SetStyle(legend, "font-size:16px");
        legend.Measure(new Size(200, 100));
        Assert.True(legend.DesiredSize.Width > zeroWidth);
        legend.Arrange(new Rect(0, 0, 200, 100));
        legend.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text == "Revenue");
    }

    [Fact]
    public void ChartTooltipKeepsChromeWithoutZeroSizedValues()
    {
        var tooltip = new ChartTooltip
        {
            SeriesTitle = "Revenue",
            XValue = "5",
            YValue = "8",
            SeriesBrush = Brushes.Black,
        };
        Css.SetStyle(tooltip, "font-size:0");
        tooltip.Measure(new Size(200, 100));
        double zeroHeight = tooltip.DesiredSize.Height;
        tooltip.Arrange(new Rect(0, 0, 200, 100));
        var drawing = new TextDrawingContext();
        tooltip.Render(drawing);
        Assert.Empty(drawing.Texts);
        Assert.Contains(drawing.Rectangles, rect => rect.Width == 8 && rect.Height == 8);

        Css.SetStyle(tooltip, "font-size:16px");
        tooltip.Measure(new Size(200, 100));
        Assert.True(tooltip.DesiredSize.Height > zeroHeight);
        tooltip.Arrange(new Rect(0, 0, 200, 100));
        tooltip.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text == "Revenue");
        Assert.Contains(drawing.Texts, text => text.Text == "X: 5");
        Assert.Contains(drawing.Texts, text => text.Text == "Y: 8");
    }

    [Fact]
    public void DockItemSuppressesZeroSizedHeaderAndRestoresIt()
    {
        var item = new DockItem { Header = "Editor" };
        Css.SetStyle(item, "font-size:0");
        item.Measure(new Size(200, 40));
        Assert.Null(item.CreateHeaderTextLayoutForTesting(200, 40));
        item.Arrange(new Rect(0, 0, 200, 40));
        var drawing = new TextDrawingContext();
        item.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(item, "font-size:16px");
        item.Measure(new Size(200, 40));
        Assert.NotNull(item.CreateHeaderTextLayoutForTesting(200, 40));
        item.Arrange(new Rect(0, 0, 200, 40));
        item.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text == "Editor");
    }

    [Fact]
    public void DataGridColumnHeaderSuppressesZeroSizedFallbackText()
    {
        var header = new DataGridColumnHeader { Content = "Name" };
        Css.SetStyle(header, "font-size:0");
        header.Measure(new Size(200, 40));
        header.Arrange(new Rect(0, 0, 200, 40));
        var drawing = new TextDrawingContext();
        header.Render(drawing);
        Assert.Empty(drawing.Texts);

        Css.SetStyle(header, "font-size:16px");
        header.Measure(new Size(200, 40));
        header.Arrange(new Rect(0, 0, 200, 40));
        header.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text == "Name");
    }

    [Fact]
    public void MarkdownCodeTextPresenterHasZeroTextMetricsAndNoHitTargetAtZeroSize()
    {
        var presenter = new MarkdownCodeTextPresenter
        {
            Code = "const value = 1;\nreturn value;",
            Padding = new Thickness(3),
            ShowLineNumbers = true,
        };
        Css.SetStyle(presenter, "font-size:0");
        presenter.Measure(new Size(240, 120));
        Assert.Equal(6, presenter.DesiredSize.Width);
        Assert.Equal(6, presenter.DesiredSize.Height);
        Assert.Equal(0, presenter.DebugGutterWidth);
        presenter.Arrange(new Rect(0, 0, 240, 120));
        var drawing = new TextDrawingContext();
        presenter.Render(drawing);
        Assert.Empty(drawing.Texts);
        Assert.False(presenter.TryHitTestCharacter(new Point(20, 20), out _));

        Css.SetStyle(presenter, "font-size:16px");
        presenter.Measure(new Size(240, 120));
        Assert.True(presenter.DesiredSize.Width > 6);
        Assert.True(presenter.DesiredSize.Height > 6);
        presenter.Arrange(new Rect(0, 0, 240, 120));
        presenter.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text.Contains("const"));
        Assert.True(presenter.TryHitTestCharacter(new Point(20, 20), out _));
    }

    [Fact]
    public void MarkdownTextPresenterSuppressesZeroSizedProseAndRestoresIt()
    {
        var presenter = new MarkdownTextPresenter
        {
            FontSizeRatio = 2,
            BoldStyle = new MarkdownInlineStyle { FontSizeRatio = 1.5 },
            Spans =
            [
                new MarkdownTextSpan("Hello ", default),
                new MarkdownTextSpan("world", new MarkdownTextStyle(true, false, false, null)),
            ],
        };
        Css.SetStyle(presenter, "font-size:0");
        presenter.Measure(new Size(240, 120));
        Assert.Equal(0, presenter.DesiredSize.Width);
        Assert.Equal(0, presenter.DesiredSize.Height);
        presenter.Arrange(new Rect(0, 0, 240, 120));
        var drawing = new TextDrawingContext();
        presenter.Render(drawing);
        Assert.Empty(drawing.Texts);
        var selectable = (IMarkdownSelectable)presenter;
        Assert.False(selectable.TryHitTestCharacter(new Point(20, 20), out _));
        Assert.Contains("Hello", selectable.GetSelectionText(0, selectable.SelectableLength));

        Css.SetStyle(presenter, "font-size:16px");
        presenter.Measure(new Size(240, 120));
        Assert.True(presenter.DesiredSize.Width > 0);
        Assert.True(presenter.DesiredSize.Height > 0);
        presenter.Arrange(new Rect(0, 0, 240, 120));
        presenter.Render(drawing);
        Assert.Contains(drawing.Texts, text => text.Text.Contains("Hello"));
        Assert.True(selectable.TryHitTestCharacter(new Point(20, 20), out _));
    }

    [Fact]
    public void MarkdownTextPresenterKeepsInlineImageAtZeroFontSize()
    {
        const string pixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO7+4u0AAAAASUVORK5CYII=";
        var image = new MarkdownInlineImage(
            new Uri($"data:image/png;base64,{pixel}"), "pixel", "pixel", null);
        var presenter = new MarkdownTextPresenter
        {
            Spans =
            [
                new MarkdownTextSpan("before", default),
                new MarkdownTextSpan("pixel", default, Image: image),
                new MarkdownTextSpan("after", default),
            ],
        };
        var child = Assert.IsType<Image>(VisualTreeHelper.GetChild(presenter, 0));
        child.Width = 10;
        child.Height = 10;
        Css.SetStyle(presenter, "font-size:0");
        presenter.Measure(new Size(240, 120));

        Assert.NotNull(child.Source);
        Assert.Equal(new Size(10, 10), child.DesiredSize);
        Assert.True(presenter.DesiredSize.Width > 0);
        Assert.True(presenter.DesiredSize.Height > 0);
        presenter.Arrange(new Rect(0, 0, 240, 120));
        Assert.Equal(new Size(10, 10), child.RenderSize);
        var drawing = new TextDrawingContext();
        presenter.Render(drawing);
        Assert.Empty(drawing.Texts);
    }

    [Fact]
    public void BulletDecoratorAlignsAgainstZeroSizedFirstLine()
    {
        var bullet = new Border { Width = 4, Height = 4 };
        var text = new TextBlock { Text = "Item" };
        var decorator = new BulletDecorator { Bullet = bullet, Child = text };
        Css.SetStyle(text, "font-size:0");
        decorator.Measure(new Size(100, 40));
        decorator.Arrange(new Rect(0, 0, 100, 40));
        Assert.Equal(0, bullet.TransformToAncestor(decorator).Y);

        Css.SetStyle(text, "font-size:16px");
        Assert.Equal(16, text.FontSize);
        decorator.InvalidateMeasure();
        decorator.Measure(new Size(100, 40));
        decorator.Arrange(new Rect(0, 0, 100, 40));
        Assert.True(bullet.TransformToAncestor(decorator).Y > 0);
    }

    private sealed class HitTestAutoCompleteBox : AutoCompleteBox
    {
        public int HitCaret(Point point) => GetCaretIndexFromPosition(point);
    }

    private sealed class TextDrawingContext : DrawingContextAdapter
    {
        public List<FormattedText> Texts { get; } = [];
        public List<(Point Start, Point End)> Lines { get; } = [];
        public List<Rect> Rectangles { get; } = [];
        public override void DrawText(FormattedText formattedText, Point origin) => Texts.Add(formattedText);
        public override void DrawLine(Pen pen, Point point0, Point point1) => Lines.Add((point0, point1));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) => Rectangles.Add(rectangle);
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle,
            double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect,
            CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
