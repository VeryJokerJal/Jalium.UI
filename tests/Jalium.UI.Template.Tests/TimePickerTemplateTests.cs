using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class TimePickerTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void TheDefaultTemplatePresentsHeaderPlaceholderSelectionAndClockStates()
    {
        var picker = Create();
        var text = Assert.IsType<TextBlock>(picker.FindName("PART_DisplayText"));
        Assert.Equal("Select a time", text.Text);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<ContentPresenter>(picker.FindName("PART_Header")).Visibility);
        picker.Header = "Departure";
        picker.SelectedTime = new TimeSpan(13, 45, 0);
        Assert.Equal("1:45 PM", text.Text);
        Assert.Equal("Selected", VisualStateManager.GetCurrentStateName(picker, "SelectionStates"));
        Assert.Equal(Visibility.Visible, Assert.IsType<ContentPresenter>(picker.FindName("PART_Header")).Visibility);
        picker.ClockIdentifier = "24HourClock";
        Assert.Equal("13:45", text.Text);
        Assert.Equal(24, picker.HourOptions.Count);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<Border>(picker.FindName("PART_PeriodPanel")).Visibility);
        picker.SelectedTime = null;
        picker.PlaceholderText = "Pick a time";
        Assert.Equal("Pick a time", text.Text);
        Assert.Equal("Placeholder", VisualStateManager.GetCurrentStateName(picker, "SelectionStates"));
    }

    [Fact]
    public void EditingOnlyCommitsOnAcceptanceAndPreservesBothBindings()
    {
        var model = new Model { Time = new TimeSpan(13, 17, 0) };
        var picker = Create();
        picker.MinuteIncrement = 15;
        picker.SetBinding(TimePicker.SelectedTimeProperty, new Binding(nameof(Model.Time)) { Source = model, Mode = BindingMode.TwoWay });
        picker.SetBinding(TimePicker.IsDropDownOpenProperty, new Binding(nameof(Model.Open)) { Source = model, Mode = BindingMode.TwoWay });
        picker.SetCurrentValue(TimePicker.IsDropDownOpenProperty, true);
        Assert.True(model.Open);
        Assert.Equal(new TimeSpan(13, 15, 0), picker.PendingTime);
        var hours = Assert.IsType<ListBox>(picker.FindName("PART_HourList"));
        var minutes = Assert.IsType<ListBox>(picker.FindName("PART_MinuteList"));
        hours.SelectedIndex = 2; // 3 PM
        minutes.SelectedIndex = 2; // :30
        Assert.Equal(new TimeSpan(15, 30, 0), picker.PendingTime);
        Assert.Equal(new TimeSpan(13, 17, 0), model.Time);
        Click(picker, "PART_CancelButton");
        Assert.False(model.Open);
        Assert.Equal(new TimeSpan(13, 17, 0), model.Time);
        picker.SetCurrentValue(TimePicker.IsDropDownOpenProperty, true);
        hours.SelectedIndex = 2;
        minutes.SelectedIndex = 2;
        Click(picker, "PART_AcceptButton");
        Assert.Equal(new TimeSpan(15, 30, 0), model.Time);
        Assert.False(model.Open);
        Assert.NotNull(picker.GetBindingExpression(TimePicker.SelectedTimeProperty));
        Assert.NotNull(picker.GetBindingExpression(TimePicker.IsDropDownOpenProperty));
    }

    [Fact]
    public void FormatAndMinuteChangesRefreshAnOpenPendingSelectionWithoutCommittingIt()
    {
        var picker = Create();
        picker.SelectedTime = new TimeSpan(23, 47, 0);
        picker.IsDropDownOpen = true;
        picker.ClockIdentifier = "24HourClock";
        Assert.Equal(23, picker.PendingHourIndex);
        picker.MinuteIncrement = 15;
        Assert.Equal(new[] { "00", "15", "30", "45" }, picker.MinuteOptions);
        Assert.Equal(new TimeSpan(23, 45, 0), picker.PendingTime);
        Assert.Equal(new TimeSpan(23, 47, 0), picker.SelectedTime);
        picker.ClockIdentifier = "12HourClock";
        Assert.Equal(10, picker.PendingHourIndex);
        Assert.Equal(1, picker.PendingPeriodIndex);
        Assert.Equal(new TimeSpan(23, 45, 0), picker.PendingTime);
        picker.IsDropDownOpen = false;
    }

    [Fact]
    public void ReplacingAnOpenTemplateDetachesOldButtonsPopupAndPlaceholderAlias()
    {
        var picker = Create();
        picker.SelectedTime = new TimeSpan(8, 30, 0);
        picker.IsDropDownOpen = true;
        picker.PendingHourIndex = 10;
        var original = picker.Template;
        var popup = Assert.IsType<Popup>(picker.FindName("PART_Popup"));
        var accept = Assert.IsAssignableFrom<ButtonBase>(picker.FindName("PART_AcceptButton"));
        var text = Assert.IsType<TextBlock>(picker.FindName("PART_DisplayText"));
        Assert.Same(picker, text.TemplatedParent);
        var empty = new ControlTemplate(typeof(TimePicker));
        empty.SetVisualTree(() => new Border());
        picker.Template = empty;
        Assert.False(popup.IsOpen);
        Assert.Null(text.TemplatedParent);
        accept.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, accept));
        Assert.Equal(new TimeSpan(8, 30, 0), picker.SelectedTime);
        picker.Template = original;
        Assert.NotSame(popup, picker.FindName("PART_Popup"));
        Assert.Equal(new TimeSpan(11, 30, 0), picker.PendingTime);
        Click(picker, "PART_AcceptButton");
        Assert.Equal(new TimeSpan(11, 30, 0), picker.SelectedTime);
        picker.Template = null;
        Assert.Null(picker.FindName("PART_Popup"));
    }

    [Fact]
    public void KeyboardWrapsAtMidnightAndDisabledAndDismissedPickersDoNotCommit()
    {
        var picker = Create();
        picker.MinuteIncrement = 15;
        picker.SelectedTime = new TimeSpan(23, 50, 0);
        Key(picker, Jalium.UI.Input.Key.Up);
        Assert.Equal(new TimeSpan(0, 5, 0), picker.SelectedTime);
        Key(picker, Jalium.UI.Input.Key.Down);
        Assert.Equal(new TimeSpan(23, 50, 0), picker.SelectedTime);
        Key(picker, Jalium.UI.Input.Key.F4);
        picker.PendingHourIndex = 5;
        Key(picker, Jalium.UI.Input.Key.Escape);
        Assert.False(picker.IsDropDownOpen);
        Assert.Equal(new TimeSpan(23, 50, 0), picker.SelectedTime);
        picker.IsDropDownOpen = true;
        var dismissTemplate = new ControlTemplate(typeof(TimePicker));
        dismissTemplate.SetVisualTree(() => new DismissiblePopup { Name = "PART_Popup" });
        picker.Template = dismissTemplate;
        Assert.IsType<DismissiblePopup>(picker.FindName("PART_Popup")).Dismiss();
        Assert.False(picker.IsDropDownOpen);
        picker.IsEnabled = false;
        Key(picker, Jalium.UI.Input.Key.Up);
        Assert.Equal(new TimeSpan(23, 50, 0), picker.SelectedTime);
        picker.IsDropDownOpen = true;
        Assert.False(picker.IsDropDownOpen);
    }

    [Fact]
    public void KeyboardInTheDetachedPopupCommitsOrCancelsWithoutReachingTheOwnerRoute()
    {
        var picker = Create();
        picker.SelectedTime = new TimeSpan(8, 30, 0);
        picker.IsDropDownOpen = true;
        picker.PendingHourIndex = 10;
        var popup = Assert.IsType<Popup>(picker.FindName("PART_Popup"));
        Key(popup.Child!, Jalium.UI.Input.Key.Enter);
        Assert.False(picker.IsDropDownOpen);
        Assert.Equal(new TimeSpan(11, 30, 0), picker.SelectedTime);
        picker.IsDropDownOpen = true;
        picker.PendingHourIndex = 0;
        Key(popup.Child!, Jalium.UI.Input.Key.Escape);
        Assert.False(picker.IsDropDownOpen);
        Assert.Equal(new TimeSpan(11, 30, 0), picker.SelectedTime);
    }

    [Fact]
    public void MultipleInstancesHaveIndependentOptionContainersAndPendingSelections()
    {
        var first = Create();
        var second = Create();
        first.IsDropDownOpen = second.IsDropDownOpen = true;
        first.PendingHourIndex = 3;
        Assert.NotEqual(first.PendingTime, second.PendingTime);
        Assert.NotSame(first.FindName("PART_HourList"), second.FindName("PART_HourList"));
        first.IsDropDownOpen = second.IsDropDownOpen = false;
    }

    private static TimePicker Create()
    {
        var picker = new TimePicker();
        picker.Resources.MergedDictionaries.Add(Defaults.Value);
        picker.Measure(new Size(240, 80));
        picker.Arrange(new Rect(0, 0, 240, 80));
        return picker;
    }
    private static void Click(TimePicker picker, string name)
    {
        var button = Assert.IsAssignableFrom<ButtonBase>(picker.FindName(name));
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }
    private static void Key(UIElement target, Jalium.UI.Input.Key key) => target.RaiseEvent(
        new Jalium.UI.Input.KeyEventArgs(Keyboard.PrimaryDevice, new TestPresentationSource { RootVisual = target }, 1, key)
        { RoutedEvent = UIElement.KeyDownEvent });
    private sealed class Model : INotifyPropertyChanged
    {
        private TimeSpan? _time;
        private bool _open;
        public TimeSpan? Time { get => _time; set { _time = value; PropertyChanged?.Invoke(this, new(nameof(Time))); } }
        public bool Open { get => _open; set { _open = value; PropertyChanged?.Invoke(this, new(nameof(Open))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private sealed class DismissiblePopup : Popup
    {
        public void Dismiss() => OnClosed(EventArgs.Empty);
    }
    private sealed class TestPresentationSource : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
}
