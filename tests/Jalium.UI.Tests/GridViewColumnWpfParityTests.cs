using System.ComponentModel;
using Jalium.UI.Controls;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class GridViewColumnWpfParityTests
{
    [Fact]
    public void TypeIsInheritableAndImplementsPropertyChanged()
    {
        Assert.False(typeof(GridViewColumn).IsSealed);
        Assert.Contains(typeof(INotifyPropertyChanged), typeof(GridViewColumn).GetInterfaces());
    }

    [Fact]
    public void DependencyPropertyChangesRaiseNotifications()
    {
        var column = new GridViewColumn();
        var names = new List<string?>();
        ((INotifyPropertyChanged)column).PropertyChanged += (_, e) => names.Add(e.PropertyName);

        column.Header = "Name";
        column.Width = 120;

        Assert.Contains(nameof(GridViewColumn.Header), names);
        Assert.Contains(nameof(GridViewColumn.Width), names);
        Assert.Equal("Name", column.ToString());
    }

    [Fact]
    public void HeaderStringFormatInvokesVirtualHook()
    {
        var column = new TrackingColumn();

        column.HeaderStringFormat = "{0:N2}";

        Assert.Null(column.OldFormat);
        Assert.Equal("{0:N2}", column.NewFormat);
    }

    private sealed class TrackingColumn : GridViewColumn
    {
        public string? OldFormat { get; private set; }
        public string? NewFormat { get; private set; }

        protected override void OnHeaderStringFormatChanged(
            string? oldHeaderStringFormat,
            string? newHeaderStringFormat)
        {
            OldFormat = oldHeaderStringFormat;
            NewFormat = newHeaderStringFormat;
        }
    }
}
