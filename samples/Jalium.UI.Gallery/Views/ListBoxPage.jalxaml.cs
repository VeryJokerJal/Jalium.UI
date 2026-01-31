using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Gallery.Views;

/// <summary>
/// Code-behind for ListBoxPage.jalxaml demonstrating ListBox functionality.
/// </summary>
public partial class ListBoxPage : Page
{
    public ListBoxPage()
    {
        InitializeComponent();

        // Populate the demo ListBox
        if (DemoListBox != null)
        {
            var items = new[] { "Apple", "Banana", "Cherry", "Date", "Elderberry", "Fig", "Grape" };
            foreach (var item in items)
            {
                DemoListBox.Items.Add(item);
            }

            DemoListBox.SelectionChanged += OnSelectionChanged;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedItemText != null && DemoListBox != null)
        {
            SelectedItemText.Text = DemoListBox.SelectedItem?.ToString() ?? "(none)";
        }
    }
}
