using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ToastNotificationTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    #region ToastNotificationItem Tests

    [Fact]
    public void ToastNotificationItem_DefaultProperties_ShouldHaveExpectedDefaults()
    {
        var toast = new ToastNotificationItem();

        Assert.Null(toast.Title);
        Assert.Null(toast.Message);
        Assert.Equal(ToastSeverity.Information, toast.Severity);
        Assert.True(toast.IsOpen);
        Assert.True(toast.IsClosable);
        Assert.True(toast.IsIconVisible);
        Assert.Null(toast.ActionButton);
        Assert.Equal(TimeSpan.FromSeconds(5), toast.Duration);
        Assert.True(toast.IsAutoDismissEnabled);
    }

    [Theory]
    [InlineData(ToastSeverity.Information)]
    [InlineData(ToastSeverity.Success)]
    [InlineData(ToastSeverity.Warning)]
    [InlineData(ToastSeverity.Error)]
    public void ToastNotificationItem_SetSeverity_ShouldUpdateProperty(ToastSeverity severity)
    {
        var toast = new ToastNotificationItem { Severity = severity };

        Assert.Equal(severity, toast.Severity);
    }

    [Fact]
    public void ToastNotificationItem_SetTitleAndMessage_ShouldUpdateProperties()
    {
        var toast = new ToastNotificationItem
        {
            Title = "Test Title",
            Message = "Test Message"
        };

        Assert.Equal("Test Title", toast.Title);
        Assert.Equal("Test Message", toast.Message);
    }

    [Fact]
    public void ToastNotificationItem_SetDuration_ShouldUpdateProperty()
    {
        var toast = new ToastNotificationItem
        {
            Duration = TimeSpan.FromSeconds(10)
        };

        Assert.Equal(TimeSpan.FromSeconds(10), toast.Duration);
    }

    [Fact]
    public void ToastNotificationItem_IsOpenFalse_ShouldRaiseClosedEvent()
    {
        var toast = new ToastNotificationItem();
        bool closedRaised = false;
        toast.Closed += (s, e) => closedRaised = true;

        toast.IsOpen = false;

        Assert.True(closedRaised);
    }

    [Fact]
    public void ToastNotificationItem_IsOpenFalse_ShouldMeasureEmpty()
    {
        var toast = new ToastNotificationItem
        {
            Title = "Test",
            Message = "Message",
            IsOpen = false
        };

        toast.Measure(new Size(400, 200));
        Assert.Equal(Size.Empty, toast.DesiredSize);
    }

    [Fact]
    public void ToastNotificationItem_IsOpenTrue_ShouldMeasureNonEmpty()
    {
        var toast = new ToastNotificationItem
        {
            Title = "Test",
            Message = "Message"
        };

        toast.Measure(new Size(400, 200));
        Assert.True(toast.DesiredSize.Height >= 56);
        Assert.True(toast.DesiredSize.Width > 0);
    }

    [Fact]
    public void ToastNotificationItem_ImplicitThemeStyle_ShouldApplyWithoutLocalOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var toast = new ToastNotificationItem
            {
                Title = "Theme Test",
                Message = "Theme-driven layout"
            };
            var host = new StackPanel { Width = 400, Height = 120 };
            host.Children.Add(toast);

            host.Measure(new Size(400, 120));
            host.Arrange(new Rect(0, 0, 400, 120));

            Assert.True(app.Resources.TryGetValue(typeof(ToastNotificationItem), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(toast.HasLocalValue(Control.PaddingProperty));
            Assert.False(toast.HasLocalValue(Control.CornerRadiusProperty));
            Assert.Equal(12, toast.Padding.Left);
            Assert.Equal(8, toast.Padding.Top);
            Assert.Equal(8, toast.CornerRadius.TopLeft);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ToastNotificationItem_SeverityBrushes_ShouldResolveFromThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var toast = new ToastNotificationItem
            {
                Severity = ToastSeverity.Warning
            };

            Assert.True(app.Resources.TryGetValue("ToastWarningBackground", out var bgObj));
            Assert.True(app.Resources.TryGetValue("ToastWarningIcon", out var iconObj));

            var getSeverityBrushes = typeof(ToastNotificationItem).GetMethod("GetSeverityBrushes",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(getSeverityBrushes);

            var tuple = getSeverityBrushes!.Invoke(toast, null);
            Assert.NotNull(tuple);

            var backgroundField = tuple!.GetType().GetField("Item1");
            var iconField = tuple.GetType().GetField("Item2");
            Assert.NotNull(backgroundField);
            Assert.NotNull(iconField);

            Assert.Same(bgObj, backgroundField!.GetValue(tuple));
            Assert.Same(iconObj, iconField!.GetValue(tuple));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Theory]
    [InlineData(ToastSeverity.Information, "ToastInformationBackground")]
    [InlineData(ToastSeverity.Success, "ToastSuccessBackground")]
    [InlineData(ToastSeverity.Warning, "ToastWarningBackground")]
    [InlineData(ToastSeverity.Error, "ToastErrorBackground")]
    public void ToastNotificationItem_AllSeverities_ShouldHaveThemeColors(ToastSeverity severity, string expectedKey)
    {
        _ = severity;
        ResetApplicationState();
        var app = new Application();

        try
        {
            Assert.True(app.Resources.TryGetValue(expectedKey, out _));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    #endregion

    #region ToastNotificationHost Tests

    [Fact]
    public void ToastNotificationHost_DefaultProperties_ShouldHaveExpectedDefaults()
    {
        var host = new ToastNotificationHost();

        Assert.Equal(ToastPosition.TopRight, host.Position);
        Assert.Equal(5, host.MaxVisibleToasts);
        Assert.Equal(8.0, host.Spacing);
        Assert.Equal(400.0, host.ToastWidth);
    }

    [Fact]
    public void ToastNotificationHost_ShowInformation_ShouldAddToast()
    {
        var host = new ToastNotificationHost();
        host.Measure(new Size(800, 600));
        host.Arrange(new Rect(0, 0, 800, 600));

        var toast = host.ShowInformation("Info Title", "Info message");

        Assert.NotNull(toast);
        Assert.Equal(ToastSeverity.Information, toast.Severity);
        Assert.Equal("Info Title", toast.Title);
        Assert.Equal("Info message", toast.Message);
        Assert.Single(host.Children);
    }

    [Fact]
    public void ToastNotificationHost_ShowSuccess_ShouldAddToast()
    {
        var host = new ToastNotificationHost();

        var toast = host.ShowSuccess("Success!", "Done.");

        Assert.NotNull(toast);
        Assert.Equal(ToastSeverity.Success, toast.Severity);
        Assert.Single(host.Children);
    }

    [Fact]
    public void ToastNotificationHost_ShowWarning_ShouldAddToast()
    {
        var host = new ToastNotificationHost();

        var toast = host.ShowWarning("Warning", "Watch out.");

        Assert.NotNull(toast);
        Assert.Equal(ToastSeverity.Warning, toast.Severity);
    }

    [Fact]
    public void ToastNotificationHost_ShowError_ShouldAddToast()
    {
        var host = new ToastNotificationHost();

        var toast = host.ShowError("Error", "Something failed.");

        Assert.NotNull(toast);
        Assert.Equal(ToastSeverity.Error, toast.Severity);
    }

    [Fact]
    public void ToastNotificationHost_MaxVisibleToasts_ShouldRemoveOldest()
    {
        var host = new ToastNotificationHost { MaxVisibleToasts = 3 };

        host.ShowInformation("Toast 1");
        host.ShowInformation("Toast 2");
        host.ShowInformation("Toast 3");
        host.ShowInformation("Toast 4");

        Assert.Equal(3, host.Children.Count);
        // The oldest (Toast 1) should have been removed
        var remaining = host.Children.Cast<ToastNotificationItem>().Select(t => t.Title).ToList();
        Assert.DoesNotContain("Toast 1", remaining);
        Assert.Contains("Toast 4", remaining);
    }

    [Fact]
    public void ToastNotificationHost_CustomDuration_ShouldApplyToToast()
    {
        var host = new ToastNotificationHost();

        var toast = host.Show(ToastSeverity.Information, "Title", "Msg", TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(10), toast.Duration);
    }

    [Theory]
    [InlineData(ToastPosition.TopRight)]
    [InlineData(ToastPosition.TopLeft)]
    [InlineData(ToastPosition.TopCenter)]
    [InlineData(ToastPosition.BottomRight)]
    [InlineData(ToastPosition.BottomLeft)]
    [InlineData(ToastPosition.BottomCenter)]
    public void ToastNotificationHost_Position_ShouldArrangeCorrectly(ToastPosition position)
    {
        var host = new ToastNotificationHost
        {
            Position = position,
            ToastWidth = 300
        };

        host.ShowInformation("Test");
        host.Measure(new Size(800, 600));
        host.Arrange(new Rect(0, 0, 800, 600));

        var child = host.Children[0];
        var arrangeRect = child.RenderSize;

        // Toast should have been arranged
        Assert.True(arrangeRect.Width > 0 || child.DesiredSize.Width > 0);
    }

    #endregion

    #region ToastService Tests

    [Fact]
    public void ToastService_SetHost_ShouldStoreHost()
    {
        var host = new ToastNotificationHost();
        ToastService.SetHost(host);

        Assert.Same(host, ToastService.Host);
    }

    [Fact]
    public void ToastService_SetHost_NullShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => ToastService.SetHost(null!));
    }

    [Fact]
    public void ToastService_ShowWithHost_ShouldCreateToast()
    {
        var host = new ToastNotificationHost();
        ToastService.SetHost(host);

        var toast = ToastService.ShowInformation("Global Info", "Message");

        Assert.NotNull(toast);
        Assert.Single(host.Children);
    }

    [Fact]
    public void ToastService_ShowWithoutHost_ShouldReturnNull()
    {
        // Reset static host
        var hostField = typeof(ToastService).GetField("s_host",
            BindingFlags.NonPublic | BindingFlags.Static);
        hostField?.SetValue(null, null);

        var toast = ToastService.ShowInformation("No host", "No message");

        Assert.Null(toast);
    }

    #endregion

    #region Platform API Tests (existing ToastNotification class)

    [Fact]
    public void ToastNotification_Constructor_ShouldSetContent()
    {
        var toast = new ToastNotification("<toast><visual /></toast>");

        Assert.Equal("<toast><visual /></toast>", toast.Content);
    }

    [Fact]
    public void ToastNotification_Constructor_NullShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => new ToastNotification(null!));
    }

    [Fact]
    public void ToastNotification_Properties_ShouldSetAndGet()
    {
        var toast = new ToastNotification("<toast />")
        {
            Tag = "tag1",
            Group = "group1",
            ExpirationTime = DateTimeOffset.UtcNow.AddHours(1),
            SuppressPopup = true,
            NotificationMirroring = NotificationMirroring.Disabled,
            RemoteId = "remote1",
            Priority = ToastNotificationPriority.High,
            PlaySound = false
        };

        Assert.Equal("tag1", toast.Tag);
        Assert.Equal("group1", toast.Group);
        Assert.NotNull(toast.ExpirationTime);
        Assert.True(toast.SuppressPopup);
        Assert.Equal(NotificationMirroring.Disabled, toast.NotificationMirroring);
        Assert.Equal("remote1", toast.RemoteId);
        Assert.Equal(ToastNotificationPriority.High, toast.Priority);
        Assert.False(toast.PlaySound);
    }

    [Fact]
    public void ToastNotification_Events_ShouldRaiseActivated()
    {
        var toast = new ToastNotification("<toast />");
        string? receivedArgs = null;
        toast.Activated += (s, e) => receivedArgs = e.Arguments;

        toast.RaiseActivated("action=clicked");

        Assert.Equal("action=clicked", receivedArgs);
    }

    [Fact]
    public void ToastNotification_Events_ShouldRaiseDismissed()
    {
        var toast = new ToastNotification("<toast />");
        ToastDismissalReason? receivedReason = null;
        toast.Dismissed += (s, e) => receivedReason = e.Reason;

        toast.RaiseDismissed(ToastDismissalReason.TimedOut);

        Assert.Equal(ToastDismissalReason.TimedOut, receivedReason);
    }

    [Fact]
    public void ToastNotification_Events_ShouldRaiseFailed()
    {
        var toast = new ToastNotification("<toast />");
        Exception? receivedError = null;
        toast.Failed += (s, e) => receivedError = e.Error;

        var error = new InvalidOperationException("test error");
        toast.RaiseFailed(error);

        Assert.Same(error, receivedError);
    }

    [Fact]
    public void ToastNotificationManager_GetTemplateContent_ShouldReturnXml()
    {
        var content = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);

        Assert.Contains("ToastText02", content);
        Assert.Contains("<text id=\"1\">", content);
        Assert.Contains("<text id=\"2\">", content);
    }

    [Fact]
    public void ToastNotificationManager_CreateToastNotifier_ShouldReturnNotifier()
    {
        var notifier = ToastNotificationManager.CreateToastNotifier();

        Assert.NotNull(notifier);
    }

    [Fact]
    public void ScheduledToastNotification_Constructor_ShouldSetProperties()
    {
        var deliveryTime = DateTimeOffset.UtcNow.AddMinutes(30);
        var scheduled = new ScheduledToastNotification("<toast />", deliveryTime)
        {
            Tag = "scheduled-tag",
            Group = "scheduled-group",
            SnoozeInterval = TimeSpan.FromMinutes(5),
            MaximumSnoozeCount = 3
        };

        Assert.Equal("<toast />", scheduled.Content);
        Assert.Equal(deliveryTime, scheduled.DeliveryTime);
        Assert.Equal("scheduled-tag", scheduled.Tag);
        Assert.Equal("scheduled-group", scheduled.Group);
        Assert.Equal(TimeSpan.FromMinutes(5), scheduled.SnoozeInterval);
        Assert.Equal(3, scheduled.MaximumSnoozeCount);
    }

    #endregion
}
