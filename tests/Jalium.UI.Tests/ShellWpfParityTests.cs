using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using Jalium.UI.Controls;
using Jalium.UI.Shell;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class ShellWpfParityTests
{
    [Fact]
    public void ShellTypesUseCanonicalNamespaceAndWpfBaseTypes()
    {
        Type[] shellTypes =
        [
            typeof(JumpItem),
            typeof(JumpItemRejectionReason),
            typeof(JumpItemsRejectedEventArgs),
            typeof(JumpItemsRemovedEventArgs),
            typeof(JumpList),
            typeof(JumpPath),
            typeof(JumpTask),
            typeof(NonClientFrameEdges),
            typeof(ResizeGripDirection),
            typeof(TaskbarItemInfo),
            typeof(TaskbarItemProgressState),
            typeof(ThumbButtonInfo),
            typeof(ThumbButtonInfoCollection),
            typeof(WindowChrome),
        ];

        Assert.All(shellTypes, type => Assert.Equal("Jalium.UI.Shell", type.Namespace));
        Assert.True(typeof(JumpList).IsSealed);
        Assert.Contains(typeof(ISupportInitialize), typeof(JumpList).GetInterfaces());
        Assert.Equal(typeof(Freezable), typeof(TaskbarItemInfo).BaseType);
        Assert.Equal(typeof(Freezable), typeof(ThumbButtonInfo).BaseType);
        Assert.Contains(typeof(Jalium.UI.Input.ICommandSource), typeof(ThumbButtonInfo).GetInterfaces());
        Assert.Equal(typeof(FreezableCollection<ThumbButtonInfo>), typeof(ThumbButtonInfoCollection).BaseType);
        Assert.Equal(typeof(Freezable), typeof(WindowChrome).BaseType);
        Assert.False(typeof(WindowChrome).IsSealed);
        Assert.False(typeof(ThumbButtonInfoCollection).IsSealed);
    }

    [Fact]
    public void PublicSignaturesMatchWpfShellSurface()
    {
        Assert.NotNull(typeof(JumpItemsRejectedEventArgs).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(JumpItemsRemovedEventArgs).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(JumpList).GetConstructor(
        [
            typeof(IEnumerable<JumpItem>),
            typeof(bool),
            typeof(bool),
        ]));
        Assert.Equal(typeof(List<JumpItem>), typeof(JumpList).GetProperty(nameof(JumpList.JumpItems))!.PropertyType);
        Assert.NotNull(typeof(JumpList).GetMethod(
            nameof(JumpList.GetJumpList),
            BindingFlags.Public | BindingFlags.Static,
            null,
            [typeof(Application)],
            null));
        Assert.NotNull(typeof(JumpList).GetMethod(
            nameof(JumpList.SetJumpList),
            BindingFlags.Public | BindingFlags.Static,
            null,
            [typeof(Application), typeof(JumpList)],
            null));
        Assert.NotNull(typeof(JumpList).GetMethod(
            nameof(JumpList.AddToRecentCategory),
            BindingFlags.Public | BindingFlags.Static,
            null,
            [typeof(JumpTask)],
            null));

        Assert.Equal(typeof(IInputElement), typeof(ThumbButtonInfo).GetProperty(nameof(ThumbButtonInfo.CommandTarget))!.PropertyType);
        Assert.Equal(typeof(DependencyProperty), typeof(ThumbButtonInfo)
            .GetField(nameof(ThumbButtonInfo.CommandTargetProperty), BindingFlags.Public | BindingFlags.Static)!
            .FieldType);

        AssertCreateInstanceCore(typeof(TaskbarItemInfo));
        AssertCreateInstanceCore(typeof(ThumbButtonInfo));
        AssertCreateInstanceCore(typeof(ThumbButtonInfoCollection));
        AssertCreateInstanceCore(typeof(WindowChrome));
    }

    [Fact]
    public void EnumValuesMatchWpfContracts()
    {
        Assert.Equal(2, (int)JumpItemRejectionReason.NoRegisteredHandler);
        Assert.Equal(3, (int)JumpItemRejectionReason.RemovedByUser);

        Assert.Equal(4, (int)ResizeGripDirection.Right);
        Assert.Equal(5, (int)ResizeGripDirection.BottomRight);
        Assert.Equal(6, (int)ResizeGripDirection.Bottom);
        Assert.Equal(7, (int)ResizeGripDirection.BottomLeft);
        Assert.Equal(8, (int)ResizeGripDirection.Left);

        Assert.Equal(1, (int)TaskbarItemProgressState.Indeterminate);
        Assert.Equal(2, (int)TaskbarItemProgressState.Normal);
    }

    [Fact]
    public void EventArgsSnapshotInputsAndValidateParallelCollections()
    {
        var item = new JumpTask { Title = "Build" };
        var items = new List<JumpItem> { item };
        var reasons = new List<JumpItemRejectionReason> { JumpItemRejectionReason.InvalidItem };
        var rejected = new JumpItemsRejectedEventArgs(items, reasons);
        var removed = new JumpItemsRemovedEventArgs(items);

        items.Clear();
        reasons.Clear();

        Assert.Same(item, Assert.Single(rejected.RejectedItems));
        Assert.Equal(JumpItemRejectionReason.InvalidItem, Assert.Single(rejected.RejectionReasons));
        Assert.Same(item, Assert.Single(removed.RemovedItems));
        Assert.Throws<NotSupportedException>(() => rejected.RejectedItems.Add(new JumpTask()));
        Assert.Empty(new JumpItemsRejectedEventArgs().RejectedItems);
        Assert.Empty(new JumpItemsRemovedEventArgs().RemovedItems);
        Assert.Throws<ArgumentException>(() => new JumpItemsRejectedEventArgs(
            [new JumpTask()],
            Array.Empty<JumpItemRejectionReason>()));
    }

    [Fact]
    public void JumpListConstructorInitializationAndValidationAreFunctional()
    {
        var validTask = new JumpTask { Title = "Compile" };
        var invalidPath = new JumpPath { Path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}") };
        var list = new JumpList([validTask, invalidPath], showFrequent: true, showRecent: false);
        JumpItemsRejectedEventArgs? rejection = null;
        list.JumpItemsRejected += (_, args) => rejection = args;

        list.Apply();

        Assert.True(list.ShowFrequentCategory);
        Assert.False(list.ShowRecentCategory);
        Assert.Same(validTask, Assert.Single(list.JumpItems));
        Assert.Same(invalidPath, Assert.Single(rejection!.RejectedItems));
        Assert.Equal(JumpItemRejectionReason.InvalidItem, Assert.Single(rejection.RejectionReasons));
        Assert.Throws<InvalidOperationException>(() => list.BeginInit());

        var initializedFromMarkup = new JumpList();
        initializedFromMarkup.BeginInit();
        Assert.Throws<InvalidOperationException>(() => initializedFromMarkup.Apply());
        initializedFromMarkup.EndInit();
        Assert.Throws<NotSupportedException>(() => initializedFromMarkup.EndInit());
    }

    [Fact]
    public void JumpListAssociationIsPerApplicationAndCanBeCleared()
    {
        ResetApplicationCurrent();
        try
        {
            var application = new Application();
            var list = new JumpList();

            JumpList.SetJumpList(application, list);
            Assert.Same(list, JumpList.GetJumpList(application));

            JumpList.SetJumpList(application, null);
            Assert.Null(JumpList.GetJumpList(application));
        }
        finally
        {
            ResetApplicationCurrent();
        }
    }

    [Fact]
    public void ShellFreezablesCloneStateAndThumbButtonExecutesCommands()
    {
        var target = new Button();
        var command = new RecordingCommand();
        var thumb = new ThumbButtonInfo
        {
            Command = command,
            CommandParameter = 42,
            CommandTarget = target,
            Description = new string('x', 300),
        };
        int clicks = 0;
        thumb.Click += (_, _) => clicks++;

        thumb.RaiseClick();

        Assert.Equal(1, clicks);
        Assert.Equal(42, command.LastParameter);
        Assert.Equal(259, thumb.Description.Length);

        var thumbClone = Assert.IsType<ThumbButtonInfo>(thumb.Clone());
        Assert.Equal(42, thumbClone.CommandParameter);
        Assert.Same(target, thumbClone.CommandTarget);
        Assert.Equal(259, thumbClone.Description.Length);

        var collection = new ThumbButtonInfoCollection { thumb };
        var collectionClone = Assert.IsType<ThumbButtonInfoCollection>(collection.Clone());
        Assert.NotSame(thumb, Assert.Single(collectionClone));

        var taskbar = new TaskbarItemInfo { ProgressValue = 0.75 };
        taskbar.ThumbButtonInfos!.Add(thumb);
        var taskbarClone = Assert.IsType<TaskbarItemInfo>(taskbar.Clone());
        Assert.Equal(0.75, taskbarClone.ProgressValue);
        Assert.NotSame(taskbar.ThumbButtonInfos, taskbarClone.ThumbButtonInfos);

        var chrome = new WindowChrome { CaptionHeight = 48 };
        var chromeClone = Assert.IsType<WindowChrome>(chrome.Clone());
        Assert.Equal(48, chromeClone.CaptionHeight);
    }

    private static void AssertCreateInstanceCore(Type type)
    {
        MethodInfo method = type.GetMethod(
            "CreateInstanceCore",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null)!;
        Assert.True(method.IsFamily);
        Assert.True(method.IsVirtual);
        Assert.Equal(typeof(Freezable), method.ReturnType);
    }

    private static void ResetApplicationCurrent() =>
        typeof(Application)
            .GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, null);

    private sealed class RecordingCommand : ICommand
    {
        public object? LastParameter { get; private set; }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => LastParameter = parameter;

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
