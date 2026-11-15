using System.Collections;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CustomControlContractTests
{
    [Fact]
    public void GeneratedContainersReleaseVisualContentBeforeRefreshRetemplateAndRemoval()
    {
        var host = new WrappedItemsControl();
        var content = new Button { Content = "Reusable page" };
        host.Items.Add(content);
        Layout();
        var first = Assert.IsType<ContentControl>(host.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Same(content, first.Content);
        host.ItemContainerStyle = new Style(typeof(ContentControl));
        Layout();
        var second = Assert.IsType<ContentControl>(host.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.NotSame(first, second);
        Assert.Null(first.Content);
        Assert.Same(content, second.Content);
        var template = new ControlTemplate(typeof(WrappedItemsControl));
        template.SetVisualTree(() => new ItemsPresenter());
        host.Template = template;
        Layout();
        Assert.Null(second.Content);
        var third = Assert.IsType<ContentControl>(host.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Same(content, third.Content);
        host.Items.Remove(content);
        Assert.Null(third.Content);
        Assert.Null(content.VisualParent);

        void Layout()
        {
            host.Measure(new Size(300, 200));
            host.Arrange(new Rect(0, 0, 300, 200));
            host.UpdateLayout();
        }
    }
    private sealed class WrappedItemsControl : ItemsControl
    {
        protected override bool IsItemItsOwnContainerOverride(object item) => false;
        protected override DependencyObject GetContainerForItemOverride() => new ContentControl();
    }

    [Fact]
    public void CssTemplateOptInPreservesNestedControlBoundariesAndCanBeRevoked()
    {
        var nestedTemplate = new ControlTemplate(typeof(Control));
        nestedTemplate.SetVisualTree(() => new Border { Name = "PART_Inner" });
        var template = new ControlTemplate(typeof(Control));
        template.SetVisualTree(() =>
        {
            var root = new StackPanel { Name = "PART_Root" };
            root.Children.Add(new Border { Name = "PART_Outer", Height = 12 });
            root.Children.Add(new Control { Name = "PART_Nested", Template = nestedTemplate });
            return root;
        });
        var control = new Control { Template = template };
        Css.SetStyleSheet(control, "Border { background-color: #ff0000; }");
        Layout();
        var outer = Assert.IsType<Border>(control.FindName("PART_Outer"));
        var nested = Assert.IsType<Control>(control.FindName("PART_Nested"));
        var inner = Assert.IsType<Border>(nested.FindName("PART_Inner"));
        Assert.Null(outer.Background);
        Css.SetIncludeTemplateParts(control, true);
        Layout();
        Assert.Equal(Color.FromRgb(255, 0, 0), Assert.IsType<SolidColorBrush>(outer.Background).Color);
        Assert.Null(inner.Background);
        Css.SetIncludeTemplateParts(control, false);
        Layout();
        Assert.Null(outer.Background);

        void Layout()
        {
            control.Measure(new Size(100, 80));
            control.Arrange(new Rect(0, 0, 100, 80));
            control.UpdateLayout();
        }
    }

    [Fact]
    public void HeaderPresentersHonorTheirOwnTypographyAndLeaveVisualContentUntouched()
    {
        var presenter = new ContentPresenter { Content = "Header" };
        Jalium.UI.Documents.TextElement.SetFontSize(presenter, 23);
        Jalium.UI.Documents.TextElement.SetFontWeight(presenter, FontWeights.Bold);
        presenter.Measure(new Size(200, 40));
        presenter.Arrange(new Rect(0, 0, 200, 40));
        var text = Assert.IsType<TextBlock>(VisualTreeHelper.GetChild(presenter, 0));
        Assert.Equal(23, text.FontSize);
        Assert.Equal(FontWeights.Bold, text.FontWeight);
        Jalium.UI.Documents.TextElement.SetFontSize(presenter, 25);
        Assert.Equal(25, text.FontSize);
        var authored = new TextBlock { Text = "Authored", FontSize = 12 };
        presenter.Content = authored;
        Jalium.UI.Documents.TextElement.SetFontSize(presenter, 30);
        Assert.Equal(12, authored.FontSize);
    }

    [Fact]
    public void SynthesizedTemplateTextUsesTheFinalAncestorForegroundAfterAttachment()
    {
        var template = new ControlTemplate(typeof(ContentControl));
        template.SetVisualTree(() => new ContentPresenter { Name = "PART_Text" });
        var control = new ContentControl { Content = "Live text", Template = template };
        control.ApplyTemplate();
        var parent = new Border { Child = control };
        Jalium.UI.Documents.TextElement.SetForeground(parent, Brushes.Purple);
        parent.Measure(new Size(300, 80));
        parent.Arrange(new Rect(0, 0, 300, 80));
        var presenter = Assert.IsType<ContentPresenter>(control.FindName("PART_Text"));
        var text = Assert.IsType<TextBlock>(VisualTreeHelper.GetChild(presenter, 0));
        Assert.Same(Brushes.Purple, text.Foreground);
        control.Foreground = Brushes.Red;
        Assert.Same(Brushes.Red, text.Foreground);
    }

    [Fact]
    public void NavigationPagesRenderTheirTemplateAboveTheirBackground()
    {
        var template = new ControlTemplate(typeof(Page));
        template.SetVisualTree(() => new Border { Background = Brushes.Red });
        var page = new Page { Background = Brushes.Blue, Template = template };
        page.Measure(new Size(40, 24));
        page.Arrange(new Rect(0, 0, 40, 24));
        var bitmap = new RenderTargetBitmap(40, 24, 96, 96, PixelFormat.Bgra32);
        bitmap.Render(page);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(10, 10, 1, 1), pixel, 4, 0);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixel);
    }

    [Fact]
    public void VisualStatesAreResolvedOnTheExpandedTemplateRoot()
    {
        var control = new Control { Template = StatefulTemplate(typeof(Control)) };
        Assert.True(VisualStateManager.GoToState(control, "Active", false));
        var root = Assert.IsType<Border>(control.FindName("PART_Root"));
        Assert.Equal(.4, root.Opacity);
        Assert.Equal("Active", VisualStateManager.GetCurrentStateName(control, "ActivityStates"));
        Assert.Equal(1, control.Opacity);
        Assert.True(VisualStateManager.GoToState(control, "Idle", false));
        Assert.Equal(1, root.Opacity);
    }

    [Fact]
    public void SharedTemplatesKeepVisualStatesIndependentPerInstance()
    {
        var template = StatefulTemplate(typeof(Control));
        var first = new Control { Template = template };
        var second = new Control { Template = template };
        first.ApplyTemplate(); second.ApplyTemplate();
        Assert.True(VisualStateManager.GoToState(first, "Active", false));
        Assert.Null(VisualStateManager.GetCurrentStateName(second, "ActivityStates"));
        Assert.Equal(1, Assert.IsType<Border>(second.FindName("PART_Root")).Opacity);
    }

    [Fact]
    public void VisualStatesRestoreTheBaselineAndRespectOtherActiveGroupsAndLocalValues()
    {
        var template = new ControlTemplate(typeof(Control));
        template.SetVisualTree(() =>
        {
            var root = new Border { Name = "PART_Root", Opacity = .8 };
            var first = new VisualStateGroup("First");
            first.States.Add(new VisualState("FirstOn") { Setters = { new Setter(UIElement.OpacityProperty, .6) } });
            first.States.Add(new VisualState("FirstOff"));
            var second = new VisualStateGroup("Second");
            second.States.Add(new VisualState("SecondOn") { Setters = { new Setter(UIElement.OpacityProperty, .4) } });
            second.States.Add(new VisualState("SecondOff"));
            VisualStateManager.SetVisualStateGroups(root, new ArrayList { first, second });
            return root;
        });
        var owner = new Control { Template = template };
        var part = Assert.IsType<Border>(owner.FindName("PART_Root"));
        Assert.True(VisualStateManager.GoToState(owner, "FirstOn", false));
        Assert.True(VisualStateManager.GoToState(owner, "SecondOn", false));
        Assert.Equal(.4, part.Opacity);
        Assert.True(VisualStateManager.GoToState(owner, "FirstOff", false));
        Assert.Equal(.4, part.Opacity);
        Assert.True(VisualStateManager.GoToState(owner, "SecondOff", false));
        Assert.Equal(.8, part.Opacity);
        part.Opacity = .9;
        Assert.True(VisualStateManager.GoToState(owner, "FirstOn", false));
        Assert.Equal(.9, part.Opacity);
        part.ClearValue(UIElement.OpacityProperty);
        Assert.Equal(.6, part.Opacity);
        owner.Template = null;
        Assert.Equal(.8, part.Opacity);
        Assert.Null(VisualStateManager.GetCurrentStateName(part, "First"));
    }

    [Fact]
    public void VisualStateBindingsAndResourcesReleaseWithoutOverwritingTheStyleBaseline()
    {
        var source = new Border { Width = .4 };
        var part = new Border
        {
            Style = new Style(typeof(Border))
            {
                Setters = { new Setter(UIElement.OpacityProperty, .9), new Setter(Border.BackgroundProperty, Brushes.Blue) }
            }
        };
        part.Resources["StateBrush"] = Brushes.Purple;
        var group = new VisualStateGroup("Activity");
        group.States.Add(new VisualState("Active")
        {
            Setters =
            {
                new Setter(UIElement.OpacityProperty, new Jalium.UI.Data.Binding
                {
                    Source = source, Path = new PropertyPath(FrameworkElement.WidthProperty)
                }),
                new Setter(Border.BackgroundProperty, new Jalium.UI.Markup.DynamicResourceReference("StateBrush"))
            }
        });
        group.States.Add(new VisualState("Idle"));
        VisualStateManager.SetVisualStateGroups(part, new ArrayList { group });
        Assert.True(VisualStateManager.GoToElementState(part, "Active", false));
        Assert.Equal(.4, part.Opacity);
        Assert.Same(Brushes.Purple, part.Background);
        source.Width = .6;
        part.Resources["StateBrush"] = Brushes.Red;
        Assert.Equal(.6, part.Opacity);
        Assert.Same(Brushes.Red, part.Background);
        Assert.True(VisualStateManager.GoToElementState(part, "Idle", false));
        Assert.Equal(.9, part.Opacity);
        Assert.Same(Brushes.Blue, part.Background);
        source.Width = .8;
        part.Resources["StateBrush"] = Brushes.Green;
        Assert.Equal(.9, part.Opacity);
        Assert.Same(Brushes.Blue, part.Background);
    }

    [Fact]
    public void APageCanReplaceAndClearTemplatesWithTheSamePartNames()
    {
        var page = new TestPage { Template = StatefulTemplate(typeof(TestPage)) };
        var oldRoot = Assert.IsType<Border>(page.FindName("PART_Root"));
        Assert.True(VisualStateManager.GoToState(page, "Active", false));
        page.Template = StatefulTemplate(typeof(TestPage));
        var root = Assert.IsType<Border>(page.FindName("PART_Root"));
        Assert.NotSame(oldRoot, root);
        Assert.Null(oldRoot.TemplatedParent);
        Assert.True(VisualStateManager.GoToState(page, "Idle", false));
        page.Template = null;
        Assert.Null(page.FindName("PART_Root"));
        Assert.True(page.Clears > 0);
    }

    [Fact]
    public void AnExternalControlCanReleasePartSubscriptionsBeforeTemplateTeardown()
    {
        var control = new TestControl { Template = PartTemplate() };
        control.ApplyTemplate();
        var old = Assert.IsType<Button>(control.FindName("PART_Action"));
        old.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, control.Invocations);
        control.Template = PartTemplate();
        control.ApplyTemplate();
        old.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, control.Invocations);
        var current = Assert.IsType<Button>(control.FindName("PART_Action"));
        current.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, control.Invocations);
        control.Template = null;
        current.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, control.Invocations);
    }

    [Fact]
    public void ConsumerStylesRetainLibraryDefaultTemplates()
    {
        var template = StatefulTemplate(typeof(LibraryControl));
        var defaults = new ResourceDictionary
        {
            [typeof(LibraryControl)] = new Style(typeof(LibraryControl))
            {
                Setters = { new Setter(Control.TemplateProperty, template), new Setter(Control.FontSizeProperty, 18d) }
            }
        };
        ThemeManager.RegisterControlThemes(defaults);
        ThemeManager.RegisterControlThemes(defaults);
        var control = new LibraryControl
        {
            Style = new Style(typeof(LibraryControl)) { Setters = { new Setter(Control.FontSizeProperty, 24d) } }
        };
        control.UpdateDefaultStyle();
        Assert.Same(template, control.Template);
        Assert.Equal(24, control.FontSize);
        Assert.False(control.HasLocalValue(Control.TemplateProperty));
    }

    [Fact]
    public void BaseThemeResourcesDoNotOverrideDerivedTemplatesButApplicationStylesDo()
    {
        var baseTemplate = StatefulTemplate(typeof(ThemeBaseControl));
        var derivedTemplate = StatefulTemplate(typeof(ThemeDerivedControl));
        var baseStyle = new Style(typeof(ThemeBaseControl)) { Setters = { new Setter(Control.TemplateProperty, baseTemplate) } };
        ThemeManager.RegisterControlThemes(new ResourceDictionary
        {
            [typeof(ThemeBaseControl)] = baseStyle,
            [typeof(ThemeDerivedControl)] = new Style(typeof(ThemeDerivedControl))
            {
                Setters = { new Setter(Control.TemplateProperty, derivedTemplate) }
            }
        });
        var host = new Border();
        host.Resources[typeof(ThemeBaseControl)] = baseStyle;
        var control = new ThemeDerivedControl();
        host.Child = control;
        host.Measure(new Size(300, 200));
        Assert.Same(derivedTemplate, control.Template);
        var applicationTemplate = StatefulTemplate(typeof(ThemeBaseControl));
        host.Resources[typeof(ThemeBaseControl)] = new Style(typeof(ThemeBaseControl))
        {
            Setters = { new Setter(Control.TemplateProperty, applicationTemplate) }
        };
        control.UpdateDefaultStyle();
        Assert.Same(applicationTemplate, control.Template);
    }
    private class ThemeBaseControl : Control;
    private sealed class ThemeDerivedControl : ThemeBaseControl;

    [Fact]
    public void WindowDefaultDimensionsAllowStyleAndBindingOverrides()
    {
        var window = new Window();
        try
        {
            Assert.Equal(800, window.Width);
            Assert.Equal(600, window.Height);
            Assert.False(window.HasLocalValue(FrameworkElement.WidthProperty));
            window.Style = new Style(typeof(Window))
            {
                Setters = { new Setter(FrameworkElement.WidthProperty, 640d), new Setter(FrameworkElement.HeightProperty, 480d) }
            };
            Assert.Equal(640, window.Width);
            Assert.Equal(480, window.Height);
            window.Width = 900;
            Assert.Equal(900, window.Width);
            window.ClearValue(FrameworkElement.WidthProperty);
            Assert.Equal(640, window.Width);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void DesktopSizeToContentMeasuresTheCurrentWindowTemplate()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()) return;
        var window = new Window { IsShowTitleBar = false, SizeToContent = SizeToContent.WidthAndHeight };
        try
        {
            var method = typeof(Window).GetMethod("UpdatePlatformSizeToContent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var first = new ControlTemplate(typeof(Window));
            first.SetVisualTree(() => new Border { Width = 143, Height = 77 });
            window.Template = first;
            method.Invoke(window, null);
            Assert.Equal(143, window.Width);
            Assert.Equal(77, window.Height);
            var second = new ControlTemplate(typeof(Window));
            second.SetVisualTree(() => new Border { Width = 209, Height = 96 });
            window.Template = second;
            method.Invoke(window, null);
            Assert.Equal(209, window.Width);
            Assert.Equal(96, window.Height);
        }
        finally { window.Close(); }
    }

    private static ControlTemplate StatefulTemplate(Type target)
    {
        var template = new ControlTemplate(target);
        template.SetVisualTree(() =>
        {
            var root = new Border { Name = "PART_Root" };
            var group = new VisualStateGroup("ActivityStates");
            group.States.Add(new VisualState("Idle"));
            group.States.Add(new VisualState("Active") { Setters = { new Setter(UIElement.OpacityProperty, .4) } });
            VisualStateManager.SetVisualStateGroups(root, new ArrayList { group });
            return root;
        });
        return template;
    }

    private static ControlTemplate PartTemplate()
    {
        var template = new ControlTemplate(typeof(TestControl));
        template.SetVisualTree(() => new Button { Name = "PART_Action" });
        return template;
    }

    private sealed class LibraryControl : Control { }

    private sealed class TestPage : Page
    {
        public int Clears { get; private set; }
        protected override void OnTemplateChanging() { Clears++; base.OnTemplateChanging(); }
    }

    private sealed class TestControl : Control
    {
        private Button? _button;
        public int Invocations { get; private set; }
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _button = GetTemplateChild("PART_Action") as Button;
            if (_button != null) _button.Click += OnClick;
        }
        protected override void OnTemplateChanging()
        {
            if (_button != null) _button.Click -= OnClick;
            _button = null;
            base.OnTemplateChanging();
        }
        private void OnClick(object sender, RoutedEventArgs args) => Invocations++;
    }
}
