using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class NonVisualTemplateStateTests
{
    public NonVisualTemplateStateTests() { ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Fact]
    public void NamedGridDefinitionsChangeLayoutAndRestoreAuthoredValues()
    {
        var control = CreateHost();
        Layout(control);
        var grid = Root(control);
        Assert.Same(grid.ColumnDefinitions[0], grid.FindName("SideColumn"));
        var side = Assert.IsType<Border>(control.FindName("Sidebar"));
        Assert.Equal(90, side.ActualWidth);
        Assert.True(VisualStateManager.GoToState(control, "Narrow", false));
        Layout(control);
        Assert.Equal(new GridLength(0), grid.ColumnDefinitions[0].Width);
        Assert.True(grid.RowDefinitions[0].Height.IsAuto);
        Assert.Equal(0, side.ActualWidth);
        VisualStateManager.GoToState(control, "Wide", false);
        Layout(control);
        Assert.Equal(new GridLength(90), grid.ColumnDefinitions[0].Width);
        Assert.Equal(new GridLength(40), grid.RowDefinitions[0].Height);
        Assert.Equal(90, side.ActualWidth);
    }

    [Fact]
    public void LocalDefinitionOverridesWinAndRetiredTemplateStateIsReleased()
    {
        var control = CreateHost();
        Layout(control);
        var column = Root(control).ColumnDefinitions[0];
        VisualStateManager.GoToState(control, "Narrow", false);
        Assert.Equal(0, column.Width.Value);
        column.Width = new GridLength(31);
        Assert.Equal(31, column.Width.Value);
        VisualStateManager.GoToState(control, "Wide", false);
        Assert.Equal(31, column.Width.Value);
        column.ClearValue(ColumnDefinition.WidthProperty);
        Assert.Equal(90, column.Width.Value);
        VisualStateManager.GoToState(control, "Narrow", false);
        var empty = new ControlTemplate(typeof(ContentControl));
        empty.SetVisualTree(static () => new Border());
        control.Template = empty;
        Layout(control);
        Assert.Equal(90, column.Width.Value);
    }

    [Fact]
    public void NonVisualResourcesKeepTemplateStateAndLocalPrecedenceDuringUpdates()
    {
        var control = CreateHost("{DynamicResource BaseWidth}", "{DynamicResource ActiveWidth}");
        control.Resources["BaseWidth"] = new GridLength(90);
        control.Resources["ActiveWidth"] = new GridLength(20);
        Layout(control);
        var column = Root(control).ColumnDefinitions[0];
        Assert.Equal(90, column.Width.Value);
        VisualStateManager.GoToState(control, "Narrow", false);
        Assert.Equal(20, column.Width.Value);
        control.Resources["ActiveWidth"] = new GridLength(35);
        control.Resources["BaseWidth"] = new GridLength(110);
        Assert.Equal(35, column.Width.Value);
        column.Width = new GridLength(51);
        control.Resources["ActiveWidth"] = new GridLength(36);
        Assert.Equal(51, column.Width.Value);
        column.ClearValue(ColumnDefinition.WidthProperty);
        Assert.Equal(36, column.Width.Value);
        control.Resources.Remove("ActiveWidth");
        Assert.Equal(110, column.Width.Value);
        control.Resources["ActiveWidth"] = new GridLength(25);
        Assert.Equal(25, column.Width.Value);
        VisualStateManager.GoToState(control, "Wide", false);
        Assert.Equal(110, column.Width.Value);
    }

    [Fact]
    public void NonVisualStateBindingsFollowTheirSourceAndDetachWhenTheStateEnds()
    {
        var control = CreateHost();
        Layout(control);
        var root = Root(control);
        var model = new WidthSource();
        var group = VisualStateManager.GetVisualStateGroups(root)!.OfType<VisualStateGroup>().Single();
        var setter = group.States.OfType<VisualState>().Single(s => s.Name == "Narrow").Setters[0];
        setter.Value = new Binding(nameof(WidthSource.Width)) { Source = model };
        VisualStateManager.GoToState(control, "Narrow", false);
        Assert.Equal(23, root.ColumnDefinitions[0].Width.Value);
        model.Width = new GridLength(47);
        Assert.Equal(47, root.ColumnDefinitions[0].Width.Value);
        VisualStateManager.GoToState(control, "Wide", false);
        model.Width = new GridLength(75);
        Assert.Equal(90, root.ColumnDefinitions[0].Width.Value);
    }

    [Fact]
    public void ProjectedGridsKeepTheirConsumerLocalDefinitionValues()
    {
        var projected = new Grid();
        var column = new ColumnDefinition { Width = new GridLength(88) };
        projected.ColumnDefinitions.Add(column);
        var host = Assert.IsType<ContentControl>(XamlReader.Parse("""
            <ContentControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <ContentControl.Template><ControlTemplate TargetType="ContentControl"><ContentPresenter /></ControlTemplate></ContentControl.Template>
            </ContentControl>
            """));
        host.Content = projected;
        Layout(host);
        Assert.Equal(new GridLength(88), column.ReadLocalValue(ColumnDefinition.WidthProperty));
    }

    private static ContentControl CreateHost(string baseline = "90", string active = "0") => Assert.IsType<ContentControl>(XamlReader.Parse("""
        <ContentControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <ContentControl.Template><ControlTemplate TargetType="ContentControl"><Grid Name="LayoutRoot">
                <VisualStateManager.VisualStateGroups><VisualStateGroup Name="LayoutStates">
                    <VisualState Name="Wide" /><VisualState Name="Narrow"><VisualState.Setters>
                        <Setter TargetName="SideColumn" Property="Width" Value="ACTIVE" />
                        <Setter TargetName="BandRow" Property="Height" Value="Auto" />
                    </VisualState.Setters></VisualState>
                </VisualStateGroup></VisualStateManager.VisualStateGroups>
                <Grid.ColumnDefinitions><ColumnDefinition x:Name="SideColumn" Width="BASELINE" /><ColumnDefinition Width="*" /></Grid.ColumnDefinitions>
                <Grid.RowDefinitions><RowDefinition x:Name="BandRow" Height="40" /><RowDefinition Height="*" /></Grid.RowDefinitions>
                <Border Name="Sidebar" Background="Red" />
            </Grid></ControlTemplate></ContentControl.Template>
        </ContentControl>
        """.Replace("BASELINE", baseline).Replace("ACTIVE", active)));
    private static Grid Root(FrameworkElement owner) => Assert.IsType<Grid>(owner.FindName("LayoutRoot"));
    private static void Layout(UIElement element) { element.Measure(new Size(320, 200)); element.Arrange(new Rect(0, 0, 320, 200)); element.UpdateLayout(); }
    private sealed class WidthSource : INotifyPropertyChanged
    {
        private GridLength _width = new(23);
        public GridLength Width { get => _width; set { _width = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Width))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
