using System.Windows.Input;
using Jalium.UI.Controls;
using Jalium.UI.DesktopDemo.Themes;
using Jalium.UI.Input;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Notifications;

namespace Jalium.UI.DesktopDemo;

public enum NotificationDemoAction { Simple, Actions, HighPriority, Silent, Image, Clear, InAppToast, PathWindow, ScrollWindow }

public sealed class NotificationDemoRequestEventArgs(NotificationDemoAction action, NotificationContent? content) : EventArgs
{
    public NotificationDemoAction Action { get; } = action;
    public NotificationContent? Content { get; } = content;
    public bool Handled { get; set; }
}

/// <summary>Notification commands and activation state, independent of the replaceable themed presentation.</summary>
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Ready", GroupName = "NotificationStates")]
[TemplateVisualState(Name = "Sent", GroupName = "NotificationStates")]
[TemplateVisualState(Name = "Error", GroupName = "NotificationStates")]
public class NotificationDemoSurface : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(NotificationDemoSurface), new PropertyMetadata("系统通知测试"));
    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(NotificationDemoSurface), new PropertyMetadata(null));
    public static readonly DependencyProperty ToastHostProperty = DependencyProperty.Register(nameof(ToastHost), typeof(ToastNotificationHost), typeof(NotificationDemoSurface), new PropertyMetadata(null));
    private static readonly DependencyPropertyKey StatusTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(StatusText), typeof(string), typeof(NotificationDemoSurface), new PropertyMetadata("点击按钮发送系统通知"));
    public static readonly DependencyProperty StatusTextProperty = StatusTextPropertyKey.DependencyProperty;
    public string Title { get => (string)GetValue(TitleProperty)!; set => SetValue(TitleProperty, value); }
    public ICommand? ActionCommand { get => (ICommand?)GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
    public ToastNotificationHost? ToastHost { get => (ToastNotificationHost?)GetValue(ToastHostProperty); set => SetValue(ToastHostProperty, value); }
    public string StatusText => (string)GetValue(StatusTextProperty)!;
    public event EventHandler<NotificationDemoRequestEventArgs>? ActionRequested;
    private readonly List<NotificationHandle> _handles = [];
    private string _notificationState = "Ready";
    private int _activationGeneration;
    public NotificationDemoSurface()
    {
        NotificationDemoTheme.Register(); DefaultStyleKey = typeof(NotificationDemoSurface);
        SetCurrentValue(ActionCommandProperty, new RelayCommand(parameter => { if (parameter is NotificationDemoAction action) RequestAction(action); }, _ => IsEnabled));
        Unloaded += (_, _) => ReleaseHandles();
    }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateStates(); }
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue) { base.OnIsEnabledChanged(oldValue, newValue); if (ActionCommand is RelayCommand relay) relay.RaiseCanExecuteChanged(); UpdateStates(); }
    public void RequestAction(NotificationDemoAction action)
    {
        if (!IsEnabled) return;
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        try
        {
            var content = CreateContent(action); var request = new NotificationDemoRequestEventArgs(action, content);
            ActionRequested?.Invoke(this, request); if (request.Handled) return;
            switch (action)
            {
                case NotificationDemoAction.Clear: SystemNotificationManager.Current.ClearAll(); ReleaseHandles(); SetStatus("✓ 已清除所有通知"); break;
                case NotificationDemoAction.InAppToast: ToastService.ShowSuccess("操作成功", "数据已保存", TimeSpan.FromSeconds(3)); SetStatus("✓ 已显示 In-App Toast"); break;
                case NotificationDemoAction.PathWindow: PathPerfWindow.Build().Show(); break;
                case NotificationDemoAction.ScrollWindow: ScrollStressWindow.Build().Show(); break;
                default:
                    var handle = SystemNotificationManager.Current.Show(content!);
                    if (action == NotificationDemoAction.Actions) { _handles.Add(handle); handle.Activated += OnActivated; }
                    SetStatus(action switch { NotificationDemoAction.Simple => "✓ 已发送简单通知", NotificationDemoAction.Actions => "✓ 已发送带按钮通知", NotificationDemoAction.HighPriority => "✓ 已发送高优先级通知", NotificationDemoAction.Silent => "✓ 已发送静默通知", _ => "✓ 已发送带图片通知" }); break;
            }
        }
        catch (Exception error) { SetValue(StatusTextPropertyKey, $"通知失败: {error.Message}"); _notificationState = "Error"; UpdateStates(); }
    }
    private void SetStatus(string value) { SetValue(StatusTextPropertyKey, value); _notificationState = "Sent"; UpdateStates(); }
    private void OnActivated(object? sender, NotificationActivatedEventArgs e)
    {
        var generation = _activationGeneration;
        Application.Current?.Dispatcher.Invoke(() => { if (generation == _activationGeneration && IsLoaded) SetStatus($"通知被点击: action={e.ActionId ?? "(body)"}"); });
        if (sender is NotificationHandle handle) { handle.Activated -= OnActivated; _handles.Remove(handle); }
    }
    private void ReleaseHandles() { _activationGeneration++; foreach (var handle in _handles) handle.Activated -= OnActivated; _handles.Clear(); }
    private void UpdateStates() { VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false); VisualStateManager.GoToState(this, _notificationState, false); }
    private static NotificationContent? CreateContent(NotificationDemoAction action) => action switch
    {
        NotificationDemoAction.Simple => new() { Title = "你好！", Body = "这是来自 Jalium.UI 的系统通知" },
        NotificationDemoAction.Actions => new() { Title = "文件下载完成", Body = "report.pdf (2.3 MB) 已保存到下载目录", Tag = "download-1", Group = "downloads", Actions = { new() { Id = "open", Label = "打开文件" }, new() { Id = "folder", Label = "打开文件夹" } } },
        NotificationDemoAction.HighPriority => new() { Title = "⚠ 磁盘空间不足", Body = "C: 盘剩余空间不足 1GB，请清理磁盘", Priority = NotificationPriority.High, Tag = "disk-warning" },
        NotificationDemoAction.Silent => new() { Title = "后台同步完成", Body = "已同步 42 个文件到云端", Silent = true, Priority = NotificationPriority.Low },
        NotificationDemoAction.Image => new() { Title = "图片通知", Body = "这条通知包含一个动态生成的图标", Icon = CreateIcon() },
        _ => null
    };
    private static BitmapImage CreateIcon()
    {
        int size = 32;
        byte[] pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = (y * size + x) * 4;
                pixels[i + 0] = (byte)(x * 8);
                pixels[i + 1] = (byte)(y * 8);
                pixels[i + 2] = 200;
                pixels[i + 3] = 255;
            }
        }

        var icon = BitmapImage.FromPixels(pixels, size, size);

        return icon;
    }
}
