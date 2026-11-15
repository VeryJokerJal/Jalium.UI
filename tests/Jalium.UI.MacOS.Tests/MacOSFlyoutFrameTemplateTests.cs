using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class MacOSFlyoutFrameTemplateTests
{
    [Fact]
    public void DefaultFrameUsesRealTemplateContentAndPreservesCallerChrome()
    {
        var frame = new FlyoutPresenter();
        Assert.Equal(0, frame.VisualChildrenCount);
        var content = new Button
        {
            Content = "Go"
        };
        frame.Content = content;
        Apply(frame);
        Layout(frame);
        var root = Assert.IsType<Border>(frame.FindName("PART_Root"));
        var presenter = Assert.IsType<ContentPresenter>(frame.FindName("PART_ContentPresenter"));
        Assert.Same(content, presenter.Content);
        frame.Background = Brushes.Navy;
        frame.Padding = new Thickness(0);
        frame.CornerRadius = new CornerRadius(0);
        frame.BorderThickness = new Thickness(0);
        Assert.Same(Brushes.Navy, root.Background);
        Assert.Equal(new Thickness(0), root.Padding);
        Assert.Equal(new Thickness(0), root.BorderThickness);
        Assert.Equal(new CornerRadius(0), root.CornerRadius);
        Assert.Same(frame, content.LogicalParentInternal);
    }

    [Fact]
    public void FlyoutUsesTheReusableFrameAndReportsLogicalEventsOnce()
    {
        var flyout = Create();
        int opened = 0, closed = 0, closing = 0;
        flyout.Opened += (_, _) => opened++;
        flyout.Closed += (_, _) => closed++;
        flyout.Closing += (_, _) => closing++;
        flyout.ShowAt(new Border());
        var frame = Assert.IsType<FlyoutPresenter>(Popup(flyout).Child);
        Assert.Same(flyout.Presenter, frame.Content);
        Assert.True(frame.IsOpen);
        Assert.Equal("Open", VisualStateManager.GetCurrentStateName(frame, "PopupStates"));
        flyout.Hide();
        flyout.Hide();
        Assert.Equal(1, opened);
        Assert.Equal(1, closed);
        Assert.Equal(1, closing);
        Assert.False(frame.IsOpen);
        Assert.Equal("Closed", VisualStateManager.GetCurrentStateName(frame, "PopupStates"));
        flyout.ShowAt(new Border());
        flyout.Hide();
        Assert.Equal(2, opened);
        Assert.Equal(2, closed);
        Assert.Equal(1, flyout.Created);
    }

    [Fact]
    public void OpenFrameRetemplateKeepsOriginalContentAndState()
    {
        var flyout = Create();
        flyout.ShowAt(new Border());
        var frame = Assert.IsType<FlyoutPresenter>(Popup(flyout).Child);
        var old = Assert.IsType<ContentPresenter>(frame.FindName("PART_ContentPresenter"));
        var template = frame.Template;
        frame.Template = Blank();
        frame.ApplyTemplate();
        Assert.True(flyout.IsOpen);
        Assert.True(frame.IsOpen);
        frame.Template = template;
        frame.ApplyTemplate();
        Layout(frame);
        var current = Assert.IsType<ContentPresenter>(frame.FindName("PART_ContentPresenter"));
        Assert.NotSame(old, current);
        Assert.Same(flyout.Presenter, current.Content);
        Assert.Equal("Open", VisualStateManager.GetCurrentStateName(frame, "PopupStates"));
        old.Measure(new Size(100, 100));
        old.Arrange(new Rect(0, 0, 100, 100));
        Assert.Same(flyout.Presenter, current.Content);
        flyout.Hide();
    }

    [Fact]
    public void TargetContextChangesUpdateCachedContentAndLeaveOldTargetsBehind()
    {
        var label = new TextBlock();
        BindingOperations.SetBinding(label, TextBlock.TextProperty, new Binding());
        var flyout = Create(new ContentControl { Content = label });
        var first = new Border
        {
            DataContext = "First"
        };
        flyout.ShowAt(first);
        var popup = Popup(flyout);
        var host = new PopupRoot(popup, popup.Child!, isLightDismiss: true);
        host.Measure(new Size(320, 160));
        host.Arrange(new Rect(0, 0, 320, 160));
        Assert.True(label.Text == "First", $"Popup={popup.DataContext}, host={host.DataContext}, frame={((FrameworkElement)popup.Child!).DataContext}, content={flyout.Presenter.DataContext}, label={label.DataContext}, text={label.Text}");
        first.DataContext = "Second";
        Assert.Equal("Second", label.Text);
        flyout.Hide();
        host.Detach();
        var next = new Border
        {
            DataContext = "Next"
        };
        flyout.ShowAt(next);
        var nextHost = new PopupRoot(popup, popup.Child!, isLightDismiss: true);
        nextHost.Measure(new Size(320, 160));
        nextHost.Arrange(new Rect(0, 0, 320, 160));
        Assert.Equal("Next", label.Text);
        first.DataContext = "Retired";
        Assert.Equal("Next", label.Text);
        flyout.Hide();
        nextHost.Detach();
    }

    [Fact]
    public void DisablingTheOpenFrameClosesTheFlyoutAndRejectsReopen()
    {
        var flyout = Create();
        int closed = 0;
        flyout.Closed += (_, _) => closed++;
        flyout.ShowAt(new Border());
        var frame = Assert.IsType<FlyoutPresenter>(Popup(flyout).Child);
        frame.IsEnabled = false;
        Assert.False(flyout.IsOpen);
        Assert.False(frame.IsOpen);
        Assert.Equal(1, closed);
        flyout.ShowAt(new Border());
        Assert.False(flyout.IsOpen);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void CallerProvidedFrameDoesNotReceiveAnotherWrapperAndStyleIsRestored()
    {
        var originalStyle = Style();
        originalStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(13)));
        var provided = new FlyoutPresenter
        {
            Content = new Button
            {
                Content = "Go"
            },
            Style = originalStyle
        };
        var flyout = new ProbeFlyout(provided);
        flyout.ShowAt(new Border());
        Assert.Same(provided, Popup(flyout).Child);
        Assert.Same(originalStyle, provided.Style);
        var overlay = Style();
        overlay.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2)));
        flyout.FlyoutPresenterStyle = overlay;
        Assert.Equal(new Thickness(2), provided.Padding);
        flyout.FlyoutPresenterStyle = null;
        Assert.Same(originalStyle, provided.Style);
        Assert.Equal(new Thickness(13), provided.Padding);
        flyout.Hide();
    }

    [Fact]
    public void MissingPartsKeepContentAndOpenIntentWithoutFallbackComposition()
    {
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(Control.TemplateProperty, Blank()));
        var flyout = new ProbeFlyout(new Button())
        {
            FlyoutPresenterStyle = style
        };
        flyout.ShowAt(new Border());
        var frame = Assert.IsType<FlyoutPresenter>(Popup(flyout).Child);
        Assert.Null(frame.FindName("PART_Root"));
        Assert.Same(flyout.Presenter, frame.Content);
        Assert.True(frame.IsOpen);
        flyout.Hide();
    }

    [Fact]
    public void MenuFlyoutKeepsItsOwnPresenterChrome()
    {
        LegacyMenuTemplateTestHelper.Register();
        var flyout = new MenuFlyout();
        flyout.Items.Add(new MenuFlyoutItem { Text = "Open" });
        flyout.ShowAt(new Border());
        Assert.IsType<MenuFlyoutPresenter>(Popup(flyout).Child);
        flyout.Hide();
    }

    [Fact]
    public void DataContentUsesPresenterTypographyAndFormatting()
    {
        var frame = new FlyoutPresenter
        {
            Content = "Flyout",
            ContentStringFormat = "Content {0}",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Navy
        };
        Apply(frame);
        Layout(frame);
        var presenter = Assert.IsType<ContentPresenter>(frame.FindName("PART_ContentPresenter"));
        var text = Assert.IsType<TextBlock>(VisualTreeHelper.GetChild(presenter, 0));
        Assert.Equal("Content Flyout", text.Text);
        Assert.Equal(28, text.FontSize);
        Assert.Equal(FontWeights.Bold, text.FontWeight);
        Assert.Same(Brushes.Navy, text.Foreground);
    }

    [Fact]
    public void ClosedObserverCanReopenTheCachedFrame()
    {
        var flyout = Create();
        var target = new Border();
        int opened = 0, closed = 0;
        flyout.Opened += (_, _) => opened++;
        flyout.Closed += (_, _) =>
        {
            closed++;
            if (closed == 1)
                flyout.ShowAt(target);
        };
        flyout.ShowAt(target);
        var frame = Assert.IsType<FlyoutPresenter>(Popup(flyout).Child);
        flyout.Hide();
        Assert.True(flyout.IsOpen);
        Assert.True(frame.IsOpen);
        Assert.Equal("Open", VisualStateManager.GetCurrentStateName(frame, "PopupStates"));
        Assert.Equal(2, opened);
        Assert.Equal(1, closed);
        flyout.Hide();
        Assert.False(frame.IsOpen);
        Assert.Equal(2, closed);
    }

    private sealed class ProbeFlyout(Control presenter) : FlyoutBase
    {
        internal Control Presenter => presenter;
        internal int Created { get; private set; }

        protected override Control CreatePresenter()
        {
            Created++;
            return presenter;
        }
    }

    private static ProbeFlyout Create(Control? content = null) => new(content ?? new Button { Content = "Go" })
    {
        FlyoutPresenterStyle = Style()
    };
    private static Style Style() => new(typeof(FlyoutPresenter))
    {
        BasedOn = (Style)MenuFlyoutTemplateTestHelper.Themes.Value[typeof(FlyoutPresenter)]
    };
    private static void Apply(FlyoutPresenter frame)
    {
        frame.Style = Style();
        frame.ApplyTemplate();
    }

    private static ControlTemplate Blank()
    {
        var t = new ControlTemplate(typeof(FlyoutPresenter));
        t.SetVisualTree(() => new Border());
        return t;
    }

    private static Popup Popup(FlyoutBase flyout) => Assert.IsType<Popup>(typeof(FlyoutBase).GetField("_popup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flyout));
    private static void Layout(Control frame)
    {
        frame.Measure(new Size(320, 160));
        frame.Arrange(new Rect(0, 0, 320, 160));
    }
}
