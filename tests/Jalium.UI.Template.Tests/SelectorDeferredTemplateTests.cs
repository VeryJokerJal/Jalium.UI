using System.Collections.ObjectModel;
using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class SelectorDeferredTemplateTests
{
    public SelectorDeferredTemplateTests() { ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Fact]
    public void ComboBoxMarkupCanSelectBeforeItsOwnContainersAreAdded()
    {
        var combo = Assert.IsType<ComboBox>(XamlReader.Parse("""
            <ComboBox xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" SelectedIndex="0">
                <ComboBoxItem Content="Forest Emerald" /><ComboBoxItem Content="Midnight Slate" />
            </ComboBox>
            """));
        Layout(combo);
        Assert.Equal(0, combo.SelectedIndex);
        Assert.Same(combo.Items[0], combo.SelectedItem);
        Assert.Equal("Forest Emerald", combo.SelectionBoxItem);
        Assert.True(Assert.IsType<ComboBoxItem>(combo.Items[0]).IsSelected);
    }

    [Fact]
    public void PositiveIndexWaitsForItsRowAndThenTracksThatOccurrence()
    {
        var source = new ObservableCollection<string>();
        var combo = new ComboBox { ItemsSource = source, SelectedIndex = 1 };
        Assert.Equal(-1, combo.SelectedIndex);
        source.Add("Alpha");
        Assert.Equal(-1, combo.SelectedIndex);
        source.Add("Beta");
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("Beta", combo.SelectionBoxItem);
        source.Insert(0, "Before");
        Assert.Equal(2, combo.SelectedIndex);
        Assert.Equal("Beta", combo.SelectedItem);
        source.Remove("Beta");
        source.Add("Later");
        Assert.Equal(-1, combo.SelectedIndex);
        Assert.Null(combo.SelectedItem);
    }

    [Fact]
    public void BoundIndexIsNotWrittenBackAsEmptyWhileItsItemsAreStillArriving()
    {
        var model = new IndexSource();
        var source = new ObservableCollection<string>();
        var combo = new ComboBox { ItemsSource = source };
        combo.SetBinding(Selector.SelectedIndexProperty, new Binding(nameof(IndexSource.Index)) { Source = model, Mode = BindingMode.TwoWay });
        Assert.Equal(1, model.Index);
        source.Add("Alpha");
        Assert.Equal(1, model.Index);
        source.Add("Beta");
        Assert.Equal("Beta", combo.SelectedItem);
        Assert.Equal(1, model.Index);
        combo.SelectedIndex = 0;
        Assert.Equal(0, model.Index);
        Assert.NotNull(combo.GetBindingExpression(Selector.SelectedIndexProperty));
    }

    [Fact]
    public void ExplicitEmptySelectionCancelsAnIndexThatIsStillWaiting()
    {
        var combo = new ComboBox { SelectedIndex = 1 };
        combo.SelectedIndex = -1;
        combo.Items.Add("Alpha");
        combo.Items.Add("Beta");
        Assert.Equal(-1, combo.SelectedIndex);
        Assert.Null(combo.SelectedItem);
    }

    [Fact]
    public void TabsDoNotAutoSelectTheFirstRowWhileADeclaredIndexIsWaiting()
    {
        var tabs = new TabControl { SelectedIndex = 1 };
        tabs.Items.Add(new TabItem { Header = "First", Content = "Alpha" });
        Assert.Equal(-1, tabs.SelectedIndex);
        tabs.Items.Add(new TabItem { Header = "Second", Content = "Beta" });
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Equal("Beta", tabs.SelectedContent);
    }

    [Fact]
    public void ResolvingTheIndexPublishesItsEffectivePropertyChangeOnce()
    {
        var selector = new ObservedSelector { SelectedIndex = 1 };
        selector.Items.Add("Alpha");
        Assert.Empty(selector.IndexChanges);
        selector.Items.Add("Beta");
        Assert.Equal([(-1, 1)], selector.IndexChanges);
        selector.Items.RemoveAt(1);
        Assert.Equal([(-1, 1), (1, -1)], selector.IndexChanges);
        selector.Items.Add("Later");
        Assert.Equal(-1, selector.SelectedIndex);
    }

    private sealed class ObservedSelector : Selector
    {
        internal List<(int Old, int New)> IndexChanges { get; } = [];
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == SelectedIndexProperty) IndexChanges.Add(((int)e.OldValue!, (int)e.NewValue!));
        }
    }

    private static void Layout(UIElement element) { element.Measure(new Size(320, 40)); element.Arrange(new Rect(0, 0, 320, 40)); element.UpdateLayout(); }
    private sealed class IndexSource : INotifyPropertyChanged
    {
        private int _index = 1;
        public int Index { get => _index; set { if (_index == value) return; _index = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Index))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
