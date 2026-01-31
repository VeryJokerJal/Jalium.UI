using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Gallery.Views;

/// <summary>
/// Code-behind for AutoCompleteBoxPage.jalxaml demonstrating AutoCompleteBox functionality.
/// </summary>
public partial class AutoCompleteBoxPage : Page
{
    public AutoCompleteBoxPage()
    {
        InitializeComponent();

        // Set up demo data
        SetupDemoData();
    }

    private void SetupDemoData()
    {
        if (DemoAutoComplete == null) return;

        // Create sample data - programming languages
        var languages = new[]
        {
            "C#",
            "C++",
            "C",
            "JavaScript",
            "TypeScript",
            "Python",
            "Java",
            "Kotlin",
            "Swift",
            "Rust",
            "Go",
            "Ruby",
            "PHP",
            "Scala",
            "F#",
            "Haskell",
            "Clojure",
            "Erlang",
            "Elixir",
            "Dart",
            "Lua",
            "R",
            "MATLAB",
            "Julia",
            "Perl"
        };

        DemoAutoComplete.ItemsSource = languages;
        DemoAutoComplete.FilterMode = AutoCompleteFilterMode.Contains;
        DemoAutoComplete.MinimumPrefixLength = 1;
        DemoAutoComplete.SelectionChanged += OnAutoCompleteSelectionChanged;
    }

    private void OnAutoCompleteSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedItemText != null && DemoAutoComplete?.SelectedItem != null)
        {
            SelectedItemText.Text = DemoAutoComplete.SelectedItem.ToString() ?? "(none)";
        }
        else if (SelectedItemText != null)
        {
            SelectedItemText.Text = "(none)";
        }
    }
}
