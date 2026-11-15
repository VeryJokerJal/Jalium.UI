using System.Collections.ObjectModel;
using System.ComponentModel;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public sealed class BindingIndexedPathTests
{
    [Fact]
    public void IndexedBindingReadsListArrayAndStringKey()
    {
        var source = new SourceModel();
        Assert.Equal("Ada", Bind(source, "Employees[0].Name").Value);
        Assert.Equal("Ada", Bind(source.Employees, "[0].Name").Value);
        Assert.Equal("Array", Bind(source, "PeopleArray[0].Name").Value);
        Assert.Equal("Home", Bind(source, "Directory[home].Name").Value);
        Assert.Equal("Dotted", Bind(source, "Directory[home.work].Name").Value);
        Assert.Equal("Custom", Bind(source, "Custom[0].Name").Value);
        Assert.Equal("missing", Bind(source, "Employees[4].Name").Value);
    }

    [Fact]
    public void IndexedBindingTracksCollectionReplacementAndSelectedItemChanges()
    {
        var source = new SourceModel();
        var target = Bind(source, "Employees[0].Name");
        Assert.Equal("Ada", target.Value);

        source.Employees[0] = new Person("Grace");
        Assert.Equal("Grace", target.Value);

        source.Employees[0].Name = "Lin";
        Assert.Equal("Lin", target.Value);

        source.Employees = new ObservableCollection<Person> { new("New") };
        Assert.Equal("New", target.Value);

        var customTarget = Bind(source, "Custom[0].Name");
        source.Custom.Replace(new Person("Indexed"));
        Assert.Equal("Indexed", customTarget.Value);
    }

    [Fact]
    public void IndexedBindingWritesBackToTheSelectedItemsProperty()
    {
        var source = new SourceModel();
        var target = new BindingTarget();
        BindingOperations.SetBinding(target, BindingTarget.ValueProperty,
            new Binding("Employees[0].Name") { Source = source, Mode = BindingMode.TwoWay });

        target.Value = "Changed";
        BindingOperations.GetBindingExpression(target, BindingTarget.ValueProperty)?.UpdateSource();
        Assert.Equal("Changed", source.Employees[0].Name);
    }

    private static BindingTarget Bind(object source, string path)
    {
        var target = new BindingTarget();
        BindingOperations.SetBinding(target, BindingTarget.ValueProperty,
            new Binding(path) { Source = source, FallbackValue = "missing", Mode = BindingMode.OneWay });
        return target;
    }

    private sealed class SourceModel : INotifyPropertyChanged
    {
        private ObservableCollection<Person> _employees = new() { new("Ada") };
        public ObservableCollection<Person> Employees
        {
            get => _employees;
            set { _employees = value; PropertyChanged?.Invoke(this, new(nameof(Employees))); }
        }

        public Person[] PeopleArray { get; } = [new("Array")];
        public Dictionary<string, Person> Directory { get; } = new()
        {
            ["home"] = new("Home"), ["home.work"] = new("Dotted")
        };
        public IndexedContainer Custom { get; } = new();
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class IndexedContainer : INotifyPropertyChanged
    {
        private Person _person = new("Custom");
        public Person this[int index] => index == 0 ? _person : throw new ArgumentOutOfRangeException(nameof(index));
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Replace(Person person)
        {
            _person = person;
            PropertyChanged?.Invoke(this, new("Item[]"));
        }
    }

    private sealed class Person(string name) : INotifyPropertyChanged
    {
        private string _name = name;
        public string Name
        {
            get => _name;
            set { _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class BindingTarget : DependencyObject
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(string), typeof(BindingTarget));

        public string? Value
        {
            get => (string?)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }
}
