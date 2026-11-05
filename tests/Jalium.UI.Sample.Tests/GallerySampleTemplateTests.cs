using Jalium.UI.Controls;
using Jalium.UI.Controls.Charts;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Documents;
using Jalium.UI.Gallery;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Sample.Tests;

public sealed class GallerySampleTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void DeferredItemsUseTheTemplateAndKeepTheirIdentityAfterReplacement()
    {
        var surface = Create(new GallerySampleSurface());
        var section = new GallerySampleSection { Title = "Examples" };
        surface.Items.Add(section); Layout(surface);
        Assert.IsType<ScrollViewer>(surface.FindName("PART_ScrollViewer"));
        Assert.IsType<ItemsPresenter>(surface.FindName("PART_ItemsPresenter"));
        var card = new GallerySampleCard { Header = "Added later", Content = new Button { Content = "Run" } };
        section.Items.Add(card); Layout(surface);
        Assert.NotNull(card.VisualParent);
        var blank = new ControlTemplate(typeof(GallerySampleSurface)); blank.SetVisualTree(() => new Border());
        surface.Template = blank; Layout(surface);
        surface.ClearValue(Control.TemplateProperty); Layout(surface);
        Assert.Same(section, Assert.Single(surface.Items.Cast<object>()));
        Assert.Same(card, Assert.Single(section.Items.Cast<object>()));
        Assert.NotNull(card.VisualParent);
    }

    [Fact]
    public void CardReleasesRetiredPresentersAndPreservesCallerContent()
    {
        var content = new Button { Content = "Caller content" };
        var card = Create(new GallerySampleCard { Header = "Title", Content = content }); Layout(card);
        var old = Assert.IsType<ContentPresenter>(card.FindName("PART_ContentPresenter"));
        Assert.Same(content, old.Content);
        var blank = new ControlTemplate(typeof(GallerySampleCard)); blank.SetVisualTree(() => new Border());
        card.Template = blank; Layout(card);
        Assert.Null(old.Content); Assert.Null(content.VisualParent);
        card.ClearValue(Control.TemplateProperty); Layout(card);
        var current = Assert.IsType<ContentPresenter>(card.FindName("PART_ContentPresenter"));
        Assert.NotSame(old, current); Assert.Same(content, current.Content);
        old.InvalidateMeasure(); old.Measure(new Size(300, 100));
        Assert.Same(current, content.VisualParent);
    }

    [Fact]
    public void IndependentButtonTemplatesKeepStateAndSurviveMissingParts()
    {
        var first = Create(new GallerySampleButtonsSection());
        var second = Create(new GallerySampleButtonsSection()); Layout(first); Layout(second);
        var toggle = Assert.IsType<ToggleButton>(first.FindName("PART_ToggleOn"));
        var other = Assert.IsType<ToggleButton>(second.FindName("PART_ToggleOn"));
        Assert.NotSame(toggle, other); Assert.True(toggle.IsChecked);
        toggle.IsChecked = false; Assert.True(other.IsChecked);
        Assert.False(Assert.IsType<Button>(first.FindName("PART_DisabledButton")).IsEnabled);
        first.IsEnabled = false;
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(first, "CommonStates"));
        var blank = new ControlTemplate(typeof(GallerySampleButtonsSection)); blank.SetVisualTree(() => new Border());
        first.Template = blank; Layout(first); first.ClearValue(Control.TemplateProperty); Layout(first);
        Assert.NotSame(toggle, first.FindName("PART_ToggleOn"));
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(first, "CommonStates"));
    }

    [Fact]
    public void NarrowCardConstrainsChromeWhileWideExamplesRemainScrollable()
    {
        var card = Create(new GallerySampleCard { PreferredWidth = 360, Content = new Border { Width = 600, Height = 40 } });
        Layout(card, 240, 150);
        Assert.Equal(240, card.DesiredSize.Width); Assert.Equal(240, card.RenderSize.Width);
        var scroll = Assert.Single(Descendants(card).OfType<ScrollViewer>());
        Assert.True(scroll.ScrollableWidth > 300);
        scroll.ScrollToHorizontalOffset(scroll.ScrollableWidth); Layout(card, 240, 150);
        Assert.True(scroll.HorizontalOffset > 300);
    }

    [Fact]
    public void FlowContentCanWrapWithinTheCardInsteadOfRequestingHorizontalScroll()
    {
        var card = Create(new GallerySampleCard
        {
            ContentScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new TextBlock { Text = new string('W', 90), TextWrapping = TextWrapping.Wrap },
        });
        Layout(card, 240, 300);
        var text = Assert.IsType<TextBlock>(card.Content); Assert.True(text.DesiredSize.Height > 30);
        Assert.Equal(0, Assert.Single(Descendants(card).OfType<ScrollViewer>()).ScrollableWidth);
    }

    [Fact]
    public void SectionDescriptionStatesFollowChangesWithoutRebuildingCards()
    {
        var section = Create(new GallerySampleSection { Title = "Category" });
        var card = new GallerySampleCard { Header = "Example" }; section.Items.Add(card); Layout(section);
        var description = Assert.IsType<TextBlock>(section.FindName("PART_Description"));
        Assert.Equal(Visibility.Collapsed, description.Visibility);
        section.Description = "Details"; Layout(section);
        Assert.Equal("Details", description.Text); Assert.Equal(Visibility.Visible, description.Visibility);
        Assert.Equal("HasDescription", VisualStateManager.GetCurrentStateName(section, "DescriptionStates"));
        Assert.Same(card, Assert.Single(section.Items.Cast<object>()));
    }

    [Fact]
    public void IndependentSelectionSectionsDoNotShareRadioGroups()
    {
        var first = Create(new GallerySampleSelectionSection());
        var second = Create(new GallerySampleSelectionSection()); Layout(first); Layout(second);
        var low = Assert.IsType<RadioButton>(first.FindName("PART_RadioLow"));
        var medium = Assert.IsType<RadioButton>(second.FindName("PART_RadioMedium"));
        Assert.NotEqual(first.RadioGroupName, second.RadioGroupName);
        Assert.Equal(first.RadioGroupName, low.GroupName); Assert.Equal(second.RadioGroupName, medium.GroupName);
        low.IsChecked = true; Assert.True(medium.IsChecked);
        Assert.False(Assert.IsType<RadioButton>(first.FindName("PART_RadioMedium")).IsChecked);
    }

    [Fact]
    public void TextInputChangesFlowBackAndSurviveTemplateReplacement()
    {
        var demo = Create(new GallerySampleTextBoxes()); Layout(demo);
        var old = Assert.IsType<TextBox>(demo.FindName("PART_SingleLine")); old.Text = "Edited by caller";
        Assert.Equal("Edited by caller", demo.SingleLineText);
        var blank = new ControlTemplate(typeof(GallerySampleTextBoxes)); blank.SetVisualTree(() => new Border());
        demo.Template = blank; Layout(demo); demo.ClearValue(Control.TemplateProperty); Layout(demo);
        var current = Assert.IsType<TextBox>(demo.FindName("PART_SingleLine"));
        Assert.NotSame(old, current); Assert.Equal("Edited by caller", current.Text);
        old.Text = "Retired template"; Assert.Equal("Edited by caller", demo.SingleLineText);
    }

    [Fact]
    public void PickersExposeDeterministicEditableValues()
    {
        var demo = Create(new GallerySamplePickersSection()); Layout(demo);
        var date = Assert.IsType<DatePicker>(demo.FindName("PART_DatePicker"));
        var time = Assert.IsType<TimePicker>(demo.FindName("PART_TimePicker"));
        var calendar = Assert.IsType<Calendar>(demo.FindName("PART_Calendar"));
        Assert.Equal(new DateTime(2026, 6, 15), calendar.SelectedDate);
        Assert.Equal(new DateTime(2026, 6, 15), calendar.DisplayDate);
        Assert.Equal(new DateTime(2026, 6, 15), date.SelectedDate);
        Assert.Equal(new TimeSpan(14, 30, 0), time.SelectedTime);
        date.SelectedDate = new DateTime(2027, 1, 2); Assert.Equal(date.SelectedDate, demo.SampleDate);
        time.SelectedTime = new TimeSpan(9, 15, 0); Assert.Equal(time.SelectedTime, demo.SampleTime);
    }

    [Fact]
    public void PasswordExampleInitializesAndRetainsTheNonDependencyProperty()
    {
        var demo = Create(new GallerySamplePasswordDemo()); Layout(demo);
        var password = Assert.IsType<PasswordBox>(demo.FindName("PART_Password"));
        Assert.Equal("S3cr3t!", password.Password);
        password.Password = "Updated sample";
        var blank = new ControlTemplate(typeof(GallerySamplePasswordDemo)); blank.SetVisualTree(() => new Border());
        demo.Template = blank; Layout(demo); Assert.Equal(string.Empty, password.Password);
        demo.ClearValue(Control.TemplateProperty); Layout(demo);
        Assert.Equal("Updated sample", Assert.IsType<PasswordBox>(demo.FindName("PART_Password")).Password);
    }

    [Fact]
    public void DeferredComboChoicesAreReplaceableWithoutRecreatingTheControl()
    {
        var demo = Create(new GallerySampleComboDemo()); Layout(demo);
        var combo = Assert.IsType<ComboBox>(demo.FindName("PART_Combo"));
        Assert.Equal("Banana", combo.SelectedItem);
        demo.Choices = new[] { "One", "Two", "Three" }; demo.SelectedIndex = 2; Layout(demo);
        Assert.Same(combo, demo.FindName("PART_Combo")); Assert.Equal("Three", combo.SelectedItem);
        combo.SelectedIndex = 0; Assert.Equal(0, demo.SelectedIndex);
    }

    [Theory]
    [InlineData(typeof(GallerySampleContainersSection), 7)]
    [InlineData(typeof(GallerySamplePanelsSection), 5)]
    [InlineData(typeof(GallerySampleNavigationSection), 8)]
    [InlineData(typeof(GallerySampleEditorsSection), 4)]
    [InlineData(typeof(GallerySampleFlyoutsSection), 4)]
    [InlineData(typeof(GallerySampleDialogsSection), 5)]
    [InlineData(typeof(GallerySampleChartsSection), 9)]
    [InlineData(typeof(GallerySampleDiagramsSection), 6)]
    [InlineData(typeof(GallerySampleTextDisplaySection), 9)]
    public void CategoryTemplatesOwnEveryDefaultCard(Type type, int count)
    {
        var demo = Create((GallerySampleHeader)Activator.CreateInstance(type)!); Layout(demo);
        Assert.False(string.IsNullOrEmpty(demo.Title));
        var section = Assert.IsType<GallerySampleSection>(demo.GetVisualChild(0));
        Assert.Equal(demo.Title, section.Title); Assert.Equal(count, section.Items.Count);
        Assert.All(section.Items.Cast<object>(), item => Assert.IsType<GallerySampleCard>(item));
        var blank = new ControlTemplate(type); blank.SetVisualTree(() => new Border());
        demo.Template = blank; Layout(demo); demo.ClearValue(Control.TemplateProperty); Layout(demo);
        Assert.Equal(count, Assert.IsType<GallerySampleSection>(demo.GetVisualChild(0)).Items.Count);
    }

    [Fact]
    public void RichDocumentSurvivesTemplateReplacementAndRetiredEditorIsDisconnected()
    {
        var document = FlowDocument.FromText("Caller document");
        var demo = Create(new GallerySampleRichTextDemo { Document = document }); Layout(demo);
        var old = Assert.IsType<RichTextBox>(demo.FindName("PART_Editor")); Assert.Same(document, old.Document);
        var blank = new ControlTemplate(typeof(GallerySampleRichTextDemo)); blank.SetVisualTree(() => new Border());
        demo.Template = blank; Layout(demo); Assert.NotSame(document, old.Document);
        demo.ClearValue(Control.TemplateProperty); Layout(demo);
        var current = Assert.IsType<RichTextBox>(demo.FindName("PART_Editor")); Assert.Same(document, current.Document);
        var replacement = FlowDocument.FromText("Replacement"); demo.Document = replacement;
        Assert.Same(replacement, current.Document); Assert.NotSame(replacement, old.Document);
    }

    [Fact]
    public void NavigationPartsKeepRealTabAndExpanderInteractions()
    {
        var demo = Create(new GallerySampleNavigationSection()); Layout(demo);
        var tabs = Assert.IsType<TabControl>(demo.FindName("PART_Tabs")); tabs.SelectedIndex = 1; Layout(demo);
        Assert.Equal("Details", Assert.IsType<TabItem>(tabs.SelectedItem).Header);
        var containers = Create(new GallerySampleContainersSection()); Layout(containers);
        var expander = Assert.IsType<Expander>(containers.FindName("PART_Expander"));
        Assert.True(expander.IsExpanded); expander.IsExpanded = false; Layout(containers); Assert.False(expander.IsExpanded);
        var scroll = Assert.IsType<ScrollViewer>(containers.FindName("PART_ExampleScroller"));
        Assert.True(scroll.ScrollableHeight > 0);
    }

    [Fact]
    public void ChartModelsStayIndependentAndReplacementReachesExistingViews()
    {
        var demo = Create(new GallerySampleChartsSection()); Layout(demo);
        var bar = Assert.IsType<BarChart>(demo.FindName("PART_BarChart"));
        var heat = Assert.IsType<Heatmap>(demo.FindName("PART_Heatmap"));
        Assert.Same(demo.Data!.Bars, bar.Series); Assert.Equal(4, Assert.Single(bar.Series).DataPoints.Count);
        Assert.Same(demo.Data.Heat, heat.Data);
        var replacement = new GallerySampleChartsData(); replacement.Bars.Clear(); demo.Data = replacement; Layout(demo);
        Assert.Same(bar, demo.FindName("PART_BarChart")); Assert.Same(replacement.Bars, bar.Series); Assert.Empty(bar.Series);
    }

    [Fact]
    public void DiagramModelsReachTheirTemplateParts()
    {
        var demo = Create(new GallerySampleDiagramsSection()); Layout(demo);
        Assert.Same(demo.Data!.SankeyNodes, Assert.IsType<SankeyDiagram>(demo.FindName("PART_Sankey")).Nodes);
        Assert.Same(demo.Data.Tasks, Assert.IsType<GanttChart>(demo.FindName("PART_Gantt")).Tasks);
        Assert.Same(demo.Data.FlowEdges, Assert.IsType<FlowchartDiagram>(demo.FindName("PART_Flowchart")).Edges);
        Assert.Equal(demo.Data.MermaidSource, Assert.IsType<MermaidDiagram>(demo.FindName("PART_Mermaid")).Source);
    }

    [Fact]
    public void BackdropTemplateOwnsTilesAndTracksEffectReplacement()
    {
        var effect = new BackdropBlurEffect(20);
        var demo = Create(new GallerySampleBackdropDemo { Material = effect }); Layout(demo, 330, 160);
        var panel = Assert.IsType<Border>(demo.FindName("PART_EffectPanel")); Assert.Same(effect, panel.BackdropEffect);
        var tiles = Assert.Single(Descendants(demo).OfType<ItemsControl>()); Assert.Equal(24, tiles.Items.Count);
        var replacement = new MicaEffect(); demo.Material = replacement; Assert.Same(replacement, panel.BackdropEffect);
        Assert.Equal(nameof(MicaEffect), demo.EffectName);
    }

    [Fact]
    public void DeferredDataExamplesKeepRowsSelectionAndHierarchy()
    {
        var listDemo = Create(new GallerySampleListBoxDemo()); Layout(listDemo);
        var list = Assert.IsType<ListBox>(listDemo.FindName("PART_List")); Assert.Equal("Drafts", list.SelectedItem);
        list.SelectedIndex = 3; Assert.Equal(3, listDemo.SelectedIndex);
        var gridDemo = Create(new GallerySampleDataGridDemo()); Layout(gridDemo);
        var grid = Assert.IsType<DataGrid>(gridDemo.FindName("PART_Grid"));
        Assert.Same(gridDemo.Rows, grid.ItemsSource); Assert.Equal(3, grid.Columns.Count);
        var treeDemo = Create(new GallerySampleTreeDemo()); Layout(treeDemo);
        var tree = Assert.IsType<TreeView>(treeDemo.FindName("PART_Tree"));
        Assert.Equal(2, tree.Items.Count);
        var solution = Assert.IsType<TreeViewItem>(tree.Items[0]); Assert.True(solution.IsExpanded); Assert.Equal(3, solution.Items.Count);
    }

    [Theory]
    [InlineData(typeof(GallerySampleQrDemo), "PART_QrCode", typeof(QRCode))]
    [InlineData(typeof(GallerySampleInkDemo), "PART_Ink", typeof(InkCanvas))]
    [InlineData(typeof(GallerySampleDiffDemo), "PART_Diff", typeof(DiffViewer))]
    [InlineData(typeof(GallerySampleJsonDemo), "PART_Json", typeof(JsonTreeViewer))]
    [InlineData(typeof(GallerySamplePropertyDemo), "PART_PropertyGrid", typeof(PropertyGrid))]
    [InlineData(typeof(GallerySampleHexDemo), "PART_HexEditor", typeof(HexEditor))]
    public void SpecializedPartsAreReplaceableAndOptional(Type type, string name, Type partType)
    {
        var demo = Create((GallerySampleDemo)Activator.CreateInstance(type)!); Layout(demo);
        var old = demo.FindName(name); Assert.IsType(partType, old);
        var blank = new ControlTemplate(type); blank.SetVisualTree(() => new Border());
        demo.Template = blank; Layout(demo); Assert.Null(demo.FindName(name));
        demo.ClearValue(Control.TemplateProperty); Layout(demo);
        Assert.IsType(partType, demo.FindName(name)); Assert.NotSame(old, demo.FindName(name));
    }

    [Fact]
    public void StreamingReplayStopsAndUnhooksRetiredButtons()
    {
        var demo = Create(new GallerySampleMarkdownReplay()); Layout(demo);
        var old = Assert.IsType<Button>(demo.FindName("PART_Replay"));
        demo.Replay(); Assert.True(demo.IsPlaying);
        Assert.Equal("Playing", VisualStateManager.GetCurrentStateName(demo, "PlaybackStates"));
        var blank = new ControlTemplate(typeof(GallerySampleMarkdownReplay)); blank.SetVisualTree(() => new Border());
        demo.Template = blank; Layout(demo); Assert.False(demo.IsPlaying);
        old.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, old)); Assert.False(demo.IsPlaying);
        demo.ClearValue(Control.TemplateProperty); Layout(demo); demo.Script = string.Empty; demo.Replay();
        Assert.False(demo.IsPlaying); Assert.Equal("Completed", VisualStateManager.GetCurrentStateName(demo, "PlaybackStates"));
    }

    [Fact]
    public void TextDisplayKeepsStyledMarkdownAndCompoundPathExamples()
    {
        var demo = Create(new GallerySampleTextDisplaySection()); Layout(demo);
        var styled = Descendants(demo).OfType<Markdown>().Single(markdown => markdown.Text.StartsWith("### Restyled", StringComparison.Ordinal));
        Assert.NotNull(styled.HeadingStyle); Assert.NotNull(styled.QuoteStyle); Assert.NotNull(styled.ListItemStyle);
        Assert.True(Descendants(demo).OfType<PathIcon>().Count(icon => icon.Width == 28) >= 6);
    }

    [Fact]
    public void NarrativeMarkdownWrapsLongParagraphsInsideItsSampleCard()
    {
        var demo = Create(new GallerySampleTextDisplaySection()); Layout(demo);
        var markdown = Descendants(demo).OfType<Markdown>().Single(item => item.Text.StartsWith("### GFM", StringComparison.Ordinal));
        markdown.Text = string.Join(" ", Enumerable.Repeat("wrapping example", 30)); Layout(demo, 480, 4000);
        var text = Assert.Single(Descendants(markdown).OfType<MarkdownTextPresenter>());
        Assert.True(text.DesiredSize.Height > markdown.FontSize * 2);
        Assert.True(text.DesiredSize.Width <= text.RenderSize.Width + .01);
    }

    private static T Create<T>(T element) where T : Control
    {
        element.Resources.MergedDictionaries.Add(Defaults.Value);
        return element;
    }

    private static void Layout(FrameworkElement element, double width = 1000, double height = 800)
    {
        element.InvalidateMeasure(); element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
    }

    private static IEnumerable<Visual> Descendants(Visual root)
    {
        for (var i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is not Visual child) continue;
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
