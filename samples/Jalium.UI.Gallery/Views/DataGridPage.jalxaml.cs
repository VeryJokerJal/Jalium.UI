using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Gallery.Views;

/// <summary>
/// Code-behind for DataGridPage.jalxaml demonstrating DataGrid functionality.
/// </summary>
public partial class DataGridPage : Page
{
    public DataGridPage()
    {
        InitializeComponent();

        // Set up demo data
        SetupDemoData();
    }

    private void SetupDemoData()
    {
        if (DemoDataGrid == null) return;

        // Create sample data
        var items = new List<PersonData>
        {
            new("Alice", "Smith", 28, "Engineering"),
            new("Bob", "Johnson", 35, "Marketing"),
            new("Charlie", "Brown", 42, "Sales"),
            new("Diana", "Williams", 31, "Engineering"),
            new("Edward", "Jones", 29, "HR"),
            new("Fiona", "Davis", 38, "Finance"),
            new("George", "Miller", 45, "Operations"),
            new("Hannah", "Wilson", 26, "Engineering")
        };

        DemoDataGrid.ItemsSource = items;
        DemoDataGrid.AutoGenerateColumns = true;
        DemoDataGrid.SelectionChanged += OnDataGridSelectionChanged;
    }

    private void OnDataGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedItemText != null && DemoDataGrid?.SelectedItem is PersonData person)
        {
            SelectedItemText.Text = $"{person.FirstName} {person.LastName}";
        }
        else if (SelectedItemText != null)
        {
            SelectedItemText.Text = "(none)";
        }
    }

    private record PersonData(string FirstName, string LastName, int Age, string Department);
}
