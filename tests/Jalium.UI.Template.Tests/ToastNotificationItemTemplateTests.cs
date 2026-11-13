using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class ToastNotificationItemTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void TemplateOwnsChromeAndWrapsLongMessagesBeforeTheCloseButton()
    {
        var toast = Create(); toast.Title = "Saved";
        toast.Message = "Your changes were saved successfully. This longer message remains readable in a narrow notification.";
        Layout(toast, 280);
        Assert.IsType<Border>(toast.GetVisualChild(0));
        var message = Assert.IsType<TextBlock>(toast.FindName("PART_MessageText"));
        var close = Assert.IsType<Button>(toast.FindName("PART_CloseButton"));
        Assert.True(message.DesiredSize.Height > toast.FontSize * 2);
        Assert.True(message.TransformToAncestor(toast).X + message.RenderSize.Width <= close.TransformToAncestor(toast).X);
        Assert.False(toast.HasLocalValue(Control.PaddingProperty));
    }

    [Fact]
    public void RetiredPartsReleaseCallerControlsAndCannotCloseOrInvokeTheToast()
    {
        var action = new Button { Content = "Undo" }; var content = new Button { Content = "Details" };
        var toast = Create(); toast.ActionButton = action; toast.Content = content; Layout(toast);
        var close = Assert.IsType<Button>(toast.FindName("PART_CloseButton"));
        var host = Assert.IsType<ContentPresenter>(toast.FindName("PART_ActionHost"));
        var actions = 0; var closes = 0;
        toast.ActionButtonClick += (_, _) => actions++; toast.CloseButtonClick += (_, _) => closes++;
        var blank = new ControlTemplate(typeof(ToastNotificationItem)); blank.SetVisualTree(() => new Border());
        toast.Template = blank; Layout(toast);
        Assert.Null(host.Content); Assert.Null(action.VisualParent); Assert.Null(content.VisualParent);
        close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, close));
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, action));
        Assert.Equal(0, closes); Assert.Equal(0, actions); Assert.True(toast.IsOpen);
        toast.ClearValue(Control.TemplateProperty); Layout(toast);
        Assert.Same(action, Assert.IsType<ContentPresenter>(toast.FindName("PART_ActionHost")).Content);
        Assert.Same(content, Assert.IsType<ContentPresenter>(toast.FindName("PART_ContentPresenter")).Content);
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, action)); Assert.Equal(1, actions);
        var currentClose = Assert.IsType<Button>(toast.FindName("PART_CloseButton"));
        currentClose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, currentClose));
        Assert.Equal(1, closes); Assert.False(toast.IsOpen);
    }

    [Fact]
    public void ReplacingAnActionUnhooksThePreviousCallerButton()
    {
        var first = new Button(); var second = new Button(); var toast = Create(); toast.ActionButton = first; Layout(toast);
        var count = 0; toast.ActionButtonClick += (_, _) => count++;
        toast.ActionButton = second; Layout(toast);
        first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, first)); Assert.Equal(0, count);
        second.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, second)); Assert.Equal(1, count);
        toast.OnApplyTemplate(); second.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, second)); Assert.Equal(2, count);
    }

    [Fact]
    public void ClosedAndOptionalStatesReleaseTheirLayoutSpace()
    {
        var toast = Create(); toast.Message = "Message"; Layout(toast);
        toast.IsClosable = false; toast.IsIconVisible = false; Layout(toast);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<Button>(toast.FindName("PART_CloseButton")).Visibility);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<SymbolIcon>(toast.FindName("PART_Icon")).Visibility);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<TextBlock>(toast.FindName("PART_TitleText")).Visibility);
        toast.IsOpen = false; Layout(toast); Assert.Equal(default, toast.DesiredSize);
        Assert.Equal("Closed", VisualStateManager.GetCurrentStateName(toast, "OpenStates"));
        toast.IsOpen = true; Layout(toast); Assert.True(toast.DesiredSize.Height >= 56);
        toast.IsEnabled = false; Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(toast, "CommonStates"));
    }

    [Fact]
    public void SeverityPaletteTracksScopedResourcesAndPreservesLocalOverrides()
    {
        var toast = Create(); toast.Resources["ToastSuccessBackground"] = Brushes.Green;
        toast.Resources["ToastSuccessIcon"] = Brushes.Blue; toast.Severity = ToastSeverity.Success; Layout(toast);
        var border = Assert.IsType<Border>(toast.FindName("RootBorder")); var icon = Assert.IsType<SymbolIcon>(toast.FindName("PART_Icon"));
        Assert.Same(Brushes.Green, border.Background); Assert.Same(Brushes.Blue, icon.Foreground); Assert.Equal(Symbol.Accept, icon.Symbol);
        toast.Resources["ToastSuccessBackground"] = Brushes.Red; Layout(toast); Assert.Same(Brushes.Red, border.Background);
        toast.Background = Brushes.Purple; toast.Severity = ToastSeverity.Error; Layout(toast);
        Assert.Same(Brushes.Purple, border.Background); Assert.Equal(Symbol.Error, icon.Symbol);
        Assert.Equal("Error", VisualStateManager.GetCurrentStateName(toast, "SeverityStates"));
    }

    [Fact]
    public void UnconstrainedMeasurementRemainsFiniteAndDismissBeforeLoadIsImmediate()
    {
        var toast = Create(); toast.Title = "Notice"; toast.Message = "Message";
        toast.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.True(double.IsFinite(toast.DesiredSize.Width)); Assert.True(double.IsFinite(toast.DesiredSize.Height));
        var closed = 0; toast.Closed += (_, _) => closed++; toast.Dismiss(); toast.Dismiss();
        Assert.False(toast.IsOpen); Assert.Equal(1, closed);
    }

    private static ToastNotificationItem Create()
    {
        var toast = new ToastNotificationItem { IsAutoDismissEnabled = false };
        toast.Resources.MergedDictionaries.Add(Defaults.Value); return toast;
    }

    private static void Layout(FrameworkElement element, double width = 320)
    {
        element.InvalidateMeasure(); element.Measure(new Size(width, 600));
        element.Arrange(new Rect(0, 0, width, element.DesiredSize.Height)); element.UpdateLayout();
    }
}
