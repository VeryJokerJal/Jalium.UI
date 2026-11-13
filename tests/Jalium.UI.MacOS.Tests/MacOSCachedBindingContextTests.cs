using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class MacOSCachedBindingContextTests
{
    [Fact]
    public void CachedLogicalChildRebindsAndReleasesPreviousSourceSubscriptions()
    {
        var first = new Source
        {
            Value = "First"
        };
        var second = new Source
        {
            Value = "Second"
        };
        var owner = new LogicalHost
        {
            DataContext = first
        };
        var text = new TextBlock();
        BindingOperations.SetBinding(text, TextBlock.TextProperty, new Binding(nameof(Source.Value)));
        owner.Add(text);
        Assert.Equal("First", text.Text);
        owner.DataContext = second;
        text.ReactivateBindings();
        Assert.Equal("Second", text.Text);
        first.Value = "Retired";
        Assert.Equal("Second", text.Text);
        second.Value = "Current";
        Assert.Equal("Current", text.Text);
    }

    [Fact]
    public void ExplicitBindingSourceRemainsIndependentOfOwnerContext()
    {
        var source = new Source
        {
            Value = "Explicit"
        };
        var owner = new LogicalHost
        {
            DataContext = new Source
            {
                Value = "Parent"
            }
        };
        var text = new TextBlock();
        BindingOperations.SetBinding(text, TextBlock.TextProperty, new Binding(nameof(Source.Value)) { Source = source });
        owner.Add(text);
        owner.DataContext = new Source
        {
            Value = "Replacement"
        };
        text.ReactivateBindings();
        Assert.Equal("Explicit", text.Text);
        source.Value = "Updated";
        Assert.Equal("Updated", text.Text);
    }

    private sealed class LogicalHost : Control
    {
        internal void Add(FrameworkElement child) => AddLogicalChild(child);
    }

    private sealed class Source : INotifyPropertyChanged
    {
        private string _value = "";
        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
