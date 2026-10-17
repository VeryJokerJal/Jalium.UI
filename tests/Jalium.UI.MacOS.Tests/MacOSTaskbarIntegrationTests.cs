using System.Windows.Input;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Media;
using Jalium.UI.Shell;

namespace Jalium.UI.Tests;

public sealed class MacOSTaskbarIntegrationTests
{
    [Fact]
    public void ModelAndNestedButtonChangesReachTheSubscription()
    {
        var info = new TaskbarItemInfo();
        int changes = 0;
        using var lease = new TaskbarItemInfoLease(() => info, () => true, () => changes++);
        lease.Refresh();
        Assert.Equal(1, changes);
        info.ProgressState = TaskbarItemProgressState.Normal;
        info.ProgressValue = 0.5;
        info.Description = "下载 · 中文🙂";
        info.Overlay = new DrawingImage();
        info.ThumbnailClipMargin = new Thickness(10);
        Assert.Equal(6, changes);
        var button = new ThumbButtonInfo();
        info.ThumbButtonInfos!.Add(button);
        int before = changes;
        button.Description = "暂停";
        button.IsEnabled = false;
        button.IsInteractive = false;
        button.Visibility = Visibility.Hidden;
        button.ImageSource = new DrawingImage();
        Assert.Equal(before + 5, changes);
        info.ThumbButtonInfos.Remove(button);
        before = changes;
        button.Description = "已移除";
        Assert.Equal(before, changes);
    }

    [Fact]
    public void ReplacementAndDisposalDetachOldModelsAndCollections()
    {
        TaskbarItemInfo? info = new();
        var old = info;
        int changes = 0;
        var lease = new TaskbarItemInfoLease(() => info, () => true, () => changes++);
        lease.Refresh();
        info = new(); lease.Refresh();
        int before = changes;
        old.ProgressValue = 0.3;
        Assert.Equal(before, changes);
        var oldButtons = info.ThumbButtonInfos;
        info.ThumbButtonInfos = new();
        before = changes;
        oldButtons!.Add(new());
        Assert.Equal(before, changes);
        lease.Dispose();
        info.Description = "after close";
        lease.Refresh();
        Assert.Equal(before, changes);
    }

    [Theory]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.NegativeInfinity, 0.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(-1.0, 0.0)]
    [InlineData(2.0, 1.0)]
    [InlineData(0.375, 0.375)]
    public void ProgressIsFiniteAndBounded(double value, double expected) =>
        Assert.Equal(expected, new TaskbarItemInfo { ProgressValue = value }.ProgressValue);

    [Fact]
    public void ProgressAndDescriptionUpdatesKeepTheSameOpenCommandUsable()
    {
        var info = new TaskbarItemInfo();
        var command = new Command();
        var button = new ThumbButtonInfo { Command = command };
        info.ThumbButtonInfos!.Add(button);
        using var lease = new TaskbarItemInfoLease(() => info, () => true, () => { });
        lease.Refresh();
        var action = lease.CaptureButton(button);
        info.ProgressValue = 0.2;
        info.Description = "下载中";
        button.Click += (_, _) => info.ProgressValue = 0.3;
        action.Invoke();
        Assert.Equal(1, command.Executions);
    }

    [Theory]
    [InlineData("model")]
    [InlineData("collection")]
    [InlineData("remove")]
    [InlineData("command")]
    [InlineData("parameter")]
    [InlineData("disabled")]
    [InlineData("hidden")]
    [InlineData("collapsed")]
    [InlineData("noninteractive")]
    [InlineData("close")]
    public void OldMenuCommandsCannotChangeAReplacementOrUnavailableTarget(string change)
    {
        var info = new TaskbarItemInfo();
        bool available = true;
        var command = new Command();
        var button = new ThumbButtonInfo { Command = command };
        info.ThumbButtonInfos!.Add(button);
        using var lease = new TaskbarItemInfoLease(() => info, () => available, () => { });
        lease.Refresh();
        var action = lease.CaptureButton(button);
        switch (change)
        {
            case "model": info = new(); lease.Refresh(); break;
            case "collection": info.ThumbButtonInfos = new(); break;
            case "remove": info.ThumbButtonInfos.Remove(button); break;
            case "command": button.Command = new Command(); break;
            case "parameter": button.CommandParameter = new object(); break;
            case "disabled": button.IsEnabled = false; break;
            case "hidden": button.Visibility = Visibility.Hidden; break;
            case "collapsed": button.Visibility = Visibility.Collapsed; break;
            case "noninteractive": button.IsInteractive = false; break;
            case "close": available = false; break;
        }
        action.Invoke();
        Assert.Equal(0, command.Executions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosingOrReplacingFromClickRetiresTheFollowingCommand(bool replace)
    {
        var info = new TaskbarItemInfo();
        bool available = true;
        var command = new Command();
        var button = new ThumbButtonInfo { Command = command };
        info.ThumbButtonInfos!.Add(button);
        using var lease = new TaskbarItemInfoLease(() => info, () => available, () => { });
        lease.Refresh();
        button.Click += (_, _) => { if (replace) { info = new(); lease.Refresh(); } else available = false; };
        lease.CaptureButton(button).Invoke();
        Assert.Equal(0, command.Executions);
    }

    [Fact]
    public void CanExecuteReentryIsCheckedBeforeCommandExecution()
    {
        var info = new TaskbarItemInfo();
        bool available = true;
        var command = new Command();
        var button = new ThumbButtonInfo { Command = command };
        info.ThumbButtonInfos!.Add(button);
        using var lease = new TaskbarItemInfoLease(() => info, () => available, () => { });
        lease.Refresh();
        command.Query = () => { available = false; return true; };
        lease.CaptureButton(button).Invoke();
        Assert.Equal(0, command.Executions);
    }

    [Fact]
    public void FrozenCloneRetainsNestedStateWithoutSharingMutableCollections()
    {
        var original = new TaskbarItemInfo { Description = "中文🙂", ProgressValue = 0.4 };
        original.ThumbButtonInfos!.Add(new ThumbButtonInfo { Description = "继续" });
        var clone = (TaskbarItemInfo)original.Clone();
        Assert.Equal(original.Description, clone.Description);
        Assert.NotSame(original.ThumbButtonInfos, clone.ThumbButtonInfos);
        Assert.NotSame(original.ThumbButtonInfos[0], clone.ThumbButtonInfos![0]);
        clone.Freeze();
        using var lease = new TaskbarItemInfoLease(() => clone, () => true, () => { });
        lease.Refresh();
        Assert.True(lease.CaptureButton(clone.ThumbButtonInfos[0]).CanInvoke);
    }

    [Fact]
    public void RemovingAndReaddingTheSameButtonDoesNotReviveAnOldMenuAction()
    {
        var info = new TaskbarItemInfo();
        var command = new Command();
        var button = new ThumbButtonInfo { Command = command };
        info.ThumbButtonInfos!.Add(button);
        using var lease = new TaskbarItemInfoLease(() => info, () => true, () => { });
        lease.Refresh();
        var old = lease.CaptureButton(button);
        info.ThumbButtonInfos.Remove(button);
        info.ThumbButtonInfos.Add(button);
        old.Invoke();
        Assert.Equal(0, command.Executions);
        lease.CaptureButton(button).Invoke();
        Assert.Equal(1, command.Executions);
    }

    [Fact]
    public void ReplacingTheModelAndRestoringItsInstanceStillRetiresAnOldAction()
    {
        var info = new TaskbarItemInfo();
        var original = info;
        var command = new Command();
        var button = new ThumbButtonInfo { Command = command };
        info.ThumbButtonInfos!.Add(button);
        using var lease = new TaskbarItemInfoLease(() => info, () => true, () => { });
        lease.Refresh();
        var old = lease.CaptureButton(button);
        info = new(); lease.Refresh();
        info = original; lease.Refresh();
        old.Invoke();
        Assert.Equal(0, command.Executions);
        lease.CaptureButton(button).Invoke();
        Assert.Equal(1, command.Executions);
    }

    [Fact]
    public void AKeptNativeActionDoesNotRetainDisposedModelContext()
    {
        var (action, context) = DisposedAction();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(context.IsAlive);
        Assert.False(action.CanInvoke);
        GC.KeepAlive(action);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (TaskbarItemInfoLease.ButtonLease, WeakReference) DisposedAction()
    {
        var context = new object();
        var info = new TaskbarItemInfo();
        var button = new ThumbButtonInfo { CommandParameter = context };
        info.ThumbButtonInfos!.Add(button);
        var lease = new TaskbarItemInfoLease(() => info, () => true, () => { });
        lease.Refresh();
        var action = lease.CaptureButton(button);
        lease.Dispose();
        return (action, new WeakReference(context));
    }

    private sealed class Command : ICommand
    {
        internal int Executions;
        internal Func<bool>? Query;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => Query?.Invoke() ?? true;
        public void Execute(object? parameter) => Executions++;
    }
}
