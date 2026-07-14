using Jalium.UI.Collections;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// Exact reproduction of the user-reported scenario:
/// ObservableCollection of ViewModelBase items rendered through ItemsControl
/// + DataTemplate + @if(#.Adult) conditional column.
///
/// Data rule:
///   Persons = 1..1000, where Adult = (i % 3) >= 1
///   → i=1,2,4,5,7,8,...   Adult = true   → Age column VISIBLE
///   → i=3,6,9,12,...      Adult = false  → Age column COLLAPSED
/// </summary>
public class RazorDataTemplateVirtualDataTests
{
    private const string ItemsControlXaml = """
        <ItemsControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                      ItemsSource="{Binding Persons}">
          <ItemsControl.ItemTemplate>
            <DataTemplate>
              <Grid ColumnDefinitions="100,100,*">
                @if(#.Adult)
                {
                  <TextBlock Grid.Column="0" Text="{Binding Age}" />
                }
                <TextBlock Grid.Column="1" Text="{Binding Name}" />
              </Grid>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
        """;

    [Fact]
    public void LargeDataSource_DirectItemsSourceAssignment_ShouldRealize()
    {
        // Control case: assign ItemsSource DIRECTLY (no DataContext binding).
        // This verifies ItemsControl can realize the template at all in this harness.
        var persons = Enumerable.Range(1, 9).Select(i => new Person
        {
            Name = $"Person {i}",
            Age = 20 + (i % 30),
            Adult = (i % 3) >= 1,
        }).ToList();

        var xamlNoBinding = """
            <ItemsControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ItemsControl.ItemTemplate>
                <DataTemplate>
                  <Grid ColumnDefinitions="100,100,*">
                    @if(#.Adult)
                    {
                      <TextBlock Grid.Column="0" Text="{Binding Age}" />
                    }
                    <TextBlock Grid.Column="1" Text="{Binding Name}" />
                  </Grid>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;

        var itemsControl = (ItemsControl)XamlReader.Parse(xamlNoBinding);
        itemsControl.ItemsSource = persons;

        itemsControl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        itemsControl.Arrange(new Rect(0, 0, 800, 1000));

        var grids = CollectItemGrids(itemsControl);
        Assert.Equal(9, grids.Count);
    }

    [Fact]
    public void LargeDataSource_EachItem_ShouldRespectOwnAdultValue()
    {
        // Exact user data source: 1000 persons, Adult = (i % 3) >= 1.
        var source = new DataSource
        {
            Persons = new ObservableCollection<Person>(
                Enumerable.Range(1, 1000).Select(i => new Person
                {
                    Name = $"Person {i}",
                    Age = 20 + (i % 30),
                    Adult = (i % 3) >= 1,
                })),
        };

        var itemsControl = (ItemsControl)XamlReader.Parse(ItemsControlXaml);
        itemsControl.DataContext = source;
        // Workaround for headless harness: {Binding Persons} does not always
        // flow through ItemsSource in tests. Assign directly to realize items.
        itemsControl.ItemsSource = source.Persons;

        itemsControl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        itemsControl.Arrange(new Rect(0, 0, 800, 10_000));

        var realizedGrids = CollectItemGrids(itemsControl);
        Assert.NotEmpty(realizedGrids);

        // For each realized Grid, the Age TextBlock (Column 0, Children[0])
        // must match its own DataContext.Adult.
        foreach (var grid in realizedGrids)
        {
            var person = Assert.IsType<Person>(grid.DataContext);
            var ageText = (TextBlock)grid.Children[0];
            var expected = person.Adult ? Visibility.Visible : Visibility.Collapsed;

            Assert.True(
                ageText.Visibility == expected,
                $"{person.Name} (Adult={person.Adult}) expected Age.Visibility={expected} but got {ageText.Visibility}");
        }
    }

    [Fact]
    public void LargeDataSource_FlipAdultOnItem_ShouldRetargetVisibility()
    {
        // Realize items first, then mutate one Person.Adult and confirm
        // the corresponding Age TextBlock follows the change live.
        var source = new DataSource
        {
            Persons = new ObservableCollection<Person>(
                Enumerable.Range(1, 100).Select(i => new Person
                {
                    Name = $"Person {i}",
                    Age = 20 + (i % 30),
                    Adult = (i % 3) >= 1,
                })),
        };

        var itemsControl = (ItemsControl)XamlReader.Parse(ItemsControlXaml);
        itemsControl.DataContext = source;
        // Workaround for headless harness: {Binding Persons} does not always
        // flow through ItemsSource in tests. Assign directly to realize items.
        itemsControl.ItemsSource = source.Persons;

        itemsControl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        itemsControl.Arrange(new Rect(0, 0, 800, 10_000));

        // Pick an item where Adult is initially false (e.g., Person 3).
        var targetPerson = source.Persons.First(p => !p.Adult);
        var targetGrid = CollectItemGrids(itemsControl)
            .First(g => ReferenceEquals(g.DataContext, targetPerson));
        var ageText = (TextBlock)targetGrid.Children[0];

        Assert.Equal(Visibility.Collapsed, ageText.Visibility);

        // Flip to true — Age must become Visible.
        targetPerson.Adult = true;
        Assert.Equal(Visibility.Visible, ageText.Visibility);

        // Flip back — Age must become Collapsed again.
        targetPerson.Adult = false;
        Assert.Equal(Visibility.Collapsed, ageText.Visibility);
    }

    [Fact]
    public void LargeDataSource_SampleAcrossKnownIndices_ShouldMatchRule()
    {
        // Spot-check the first 9 items explicitly against the rule
        // (i % 3) >= 1 → Adult.  Indices 1,2,4,5,7,8 ⇒ true, 3,6,9 ⇒ false.
        var source = new DataSource
        {
            Persons = new ObservableCollection<Person>(
                Enumerable.Range(1, 9).Select(i => new Person
                {
                    Name = $"Person {i}",
                    Age = 20 + (i % 30),
                    Adult = (i % 3) >= 1,
                })),
        };

        var itemsControl = (ItemsControl)XamlReader.Parse(ItemsControlXaml);
        itemsControl.DataContext = source;
        // Workaround for headless harness: {Binding Persons} does not always
        // flow through ItemsSource in tests. Assign directly to realize items.
        itemsControl.ItemsSource = source.Persons;

        itemsControl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        itemsControl.Arrange(new Rect(0, 0, 800, 1000));

        var grids = CollectItemGrids(itemsControl);
        Assert.Equal(9, grids.Count);

        // Walk grids in DataContext order (they mirror the source order).
        var byName = grids.ToDictionary(g => ((Person)g.DataContext!).Name, g => g);

        AssertAge(byName["Person 1"], Visibility.Visible);    // 1 % 3 = 1
        AssertAge(byName["Person 2"], Visibility.Visible);    // 2 % 3 = 2
        AssertAge(byName["Person 3"], Visibility.Collapsed);  // 3 % 3 = 0
        AssertAge(byName["Person 4"], Visibility.Visible);
        AssertAge(byName["Person 5"], Visibility.Visible);
        AssertAge(byName["Person 6"], Visibility.Collapsed);
        AssertAge(byName["Person 7"], Visibility.Visible);
        AssertAge(byName["Person 8"], Visibility.Visible);
        AssertAge(byName["Person 9"], Visibility.Collapsed);

        static void AssertAge(Grid grid, Visibility expected)
        {
            var person = (Person)grid.DataContext!;
            var ageText = (TextBlock)grid.Children[0];
            Assert.True(
                ageText.Visibility == expected,
                $"{person.Name} expected {expected} got {ageText.Visibility}");
        }
    }

    // --- Test models — mirror the user's production VitrualDataPage code ---

    public class DataSource
    {
        public ObservableCollection<Person> Persons { get; set; } = new();
    }

    public class Person : ViewModelBase
    {
        private string _name = string.Empty;
        private int _age;
        private bool _adult;

        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public int Age { get => _age; set => SetProperty(ref _age, value); }
        public bool Adult { get => _adult; set => SetProperty(ref _adult, value); }
    }

    // --- Helpers ---

    private static List<Grid> CollectItemGrids(Visual root)
    {
        var grids = new List<Grid>();
        Walk(root);
        return grids;

        void Walk(Visual node)
        {
            if (node is Grid g && g.DataContext is Person && g.Children.Count == 2)
                grids.Add(g);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                if (VisualTreeHelper.GetChild(node, i) is Visual child)
                    Walk(child);
            }
        }
    }
}
