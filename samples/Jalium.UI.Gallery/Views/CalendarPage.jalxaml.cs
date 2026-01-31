using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Gallery.Views;

/// <summary>
/// Code-behind for CalendarPage.jalxaml demonstrating Calendar functionality.
/// </summary>
public partial class CalendarPage : Page
{
    public CalendarPage()
    {
        InitializeComponent();
        SetupDemo();
    }

    private void SetupDemo()
    {
        if (DemoCalendar != null)
        {
            DemoCalendar.SelectedDateChanged += OnSelectedDateChanged;
        }
    }

    private void OnSelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedDateText != null && DemoCalendar?.SelectedDate != null)
        {
            SelectedDateText.Text = DemoCalendar.SelectedDate.Value.ToString("yyyy-MM-dd");
        }
        else if (SelectedDateText != null)
        {
            SelectedDateText.Text = "None";
        }
    }
}
