using System.ComponentModel;
using Jalium.UI.Controls;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class GroupStyleWpfParityTests
{
    [Fact]
    public void GroupStyleIsInheritableAndImplementsPropertyChanged()
    {
        Assert.False(typeof(GroupStyle).IsSealed);
        Assert.Contains(typeof(INotifyPropertyChanged), typeof(GroupStyle).GetInterfaces());
    }

    [Fact]
    public void PropertySettersRaiseNotificationsOnlyForChanges()
    {
        var style = new GroupStyle();
        var changed = new List<string?>();
        ((INotifyPropertyChanged)style).PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        style.HidesIfEmpty = true;
        style.HidesIfEmpty = true;
        style.AlternationCount = 3;
        style.HeaderStringFormat = "{0}";

        Assert.Equal(
            [nameof(GroupStyle.HidesIfEmpty), nameof(GroupStyle.AlternationCount), nameof(GroupStyle.HeaderStringFormat)],
            changed);
    }

    [Fact]
    public void DerivedTypeCanObserveProtectedNotificationHook()
    {
        var style = new TrackingGroupStyle();
        style.Panel = new ItemsPanelTemplate();

        Assert.Equal(nameof(GroupStyle.Panel), style.LastPropertyName);
    }

    [Fact]
    public void DefaultGroupPanelCreatesAStackPanelAndIsSealed()
    {
        Assert.True(GroupStyle.DefaultGroupPanel.IsSealed);
        Assert.IsType<StackPanel>(GroupStyle.DefaultGroupPanel.CreatePanel());
    }

    private sealed class TrackingGroupStyle : GroupStyle
    {
        public string? LastPropertyName { get; private set; }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            LastPropertyName = e.PropertyName;
            base.OnPropertyChanged(e);
        }
    }
}
