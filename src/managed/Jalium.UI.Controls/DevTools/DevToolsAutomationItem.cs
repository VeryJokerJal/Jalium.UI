using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Jalium.UI.Automation.Peers;

namespace Jalium.UI.Controls.DevTools;

/// <summary>The semantic presentation of an automation property.</summary>
public enum DevToolsAutomationValueKind { Text, String, Boolean, Number, Enum, Error }

/// <summary>An immutable property snapshot, independent of a live automation provider.</summary>
public sealed record DevToolsAutomationProperty(string Name, string Value, DevToolsAutomationValueKind Kind = DevToolsAutomationValueKind.Text)
{
    static DevToolsAutomationProperty()
    {
        PropertyAccessorRegistry.Register(typeof(DevToolsAutomationProperty), nameof(Name), item => ((DevToolsAutomationProperty)item).Name);
        PropertyAccessorRegistry.Register(typeof(DevToolsAutomationProperty), nameof(Value), item => ((DevToolsAutomationProperty)item).Value);
        PropertyAccessorRegistry.Register(typeof(DevToolsAutomationProperty), nameof(Kind), item => ((DevToolsAutomationProperty)item).Kind);
    }
}

/// <summary>A bindable automation-tree snapshot with independently expandable children.</summary>
public class DevToolsAutomationItem : DependencyObject, INotifyPropertyChanged
{
    private static readonly ConditionalWeakTable<AutomationPeer, object> Identities = new();
    private WeakReference<AutomationPeer>? _peer;
    internal object? Identity { get; private set; }

    private static DependencyProperty Property(string name, Type type, object? value) =>
        DependencyProperty.Register(name, type, typeof(DevToolsAutomationItem), new PropertyMetadata(value));
    public static readonly DependencyProperty PeerNameProperty = Property(nameof(PeerName), typeof(string), string.Empty);
    public static readonly DependencyProperty ClassNameProperty = Property(nameof(ClassName), typeof(string), string.Empty);
    public static readonly DependencyProperty RoleProperty = Property(nameof(Role), typeof(string), string.Empty);
    public static readonly DependencyProperty NoteProperty = Property(nameof(Note), typeof(string), string.Empty);
    public static readonly DependencyProperty IsIssueProperty = Property(nameof(IsIssue), typeof(bool), false);
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(DevToolsAutomationItem),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(DevToolsAutomationItem),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    private static readonly DependencyPropertyKey ChildrenPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Children), typeof(ObservableCollection<DevToolsAutomationItem>), typeof(DevToolsAutomationItem), new PropertyMetadata(null));
    public static readonly DependencyProperty ChildrenProperty = ChildrenPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PropertiesPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Properties), typeof(ObservableCollection<DevToolsAutomationProperty>), typeof(DevToolsAutomationItem), new PropertyMetadata(null));
    public static readonly DependencyProperty PropertiesProperty = PropertiesPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PatternsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Patterns), typeof(ObservableCollection<string>), typeof(DevToolsAutomationItem), new PropertyMetadata(null));
    public static readonly DependencyProperty PatternsProperty = PatternsPropertyKey.DependencyProperty;

    static DevToolsAutomationItem()
    {
        PropertyAccessorRegistry.Register(typeof(DevToolsAutomationItem), nameof(DisplayName), item => ((DevToolsAutomationItem)item).DisplayName);
        PropertyAccessorRegistry.Register(typeof(DevToolsAutomationItem), nameof(AutomationName), item => ((DevToolsAutomationItem)item).AutomationName);
        PropertyAccessorRegistry.Register(typeof(DevToolsAutomationItem), nameof(Children), item => ((DevToolsAutomationItem)item).Children);
        PropertyAccessorRegistry.Register(typeof(DevToolsAutomationItem), nameof(IsExpanded), item => ((DevToolsAutomationItem)item).IsExpanded);
        PropertyAccessorRegistry.RegisterSetter(typeof(DevToolsAutomationItem), nameof(IsExpanded), (item, value) => ((DevToolsAutomationItem)item).IsExpanded = value is true);
        PropertyAccessorRegistry.Register(typeof(DevToolsAutomationItem), nameof(IsSelected), item => ((DevToolsAutomationItem)item).IsSelected);
        PropertyAccessorRegistry.RegisterSetter(typeof(DevToolsAutomationItem), nameof(IsSelected), (item, value) => ((DevToolsAutomationItem)item).IsSelected = value is true);
    }
    public DevToolsAutomationItem()
    {
        SetValue(ChildrenPropertyKey, new ObservableCollection<DevToolsAutomationItem>());
        SetValue(PropertiesPropertyKey, new ObservableCollection<DevToolsAutomationProperty>());
        SetValue(PatternsPropertyKey, new ObservableCollection<string>());
    }
    public string PeerName { get => (string?)GetValue(PeerNameProperty) ?? string.Empty; set => SetValue(PeerNameProperty, value); }
    public string ClassName { get => (string?)GetValue(ClassNameProperty) ?? string.Empty; set => SetValue(ClassNameProperty, value); }
    public string Role { get => (string?)GetValue(RoleProperty) ?? string.Empty; set => SetValue(RoleProperty, value); }
    public string Note { get => (string?)GetValue(NoteProperty) ?? string.Empty; set => SetValue(NoteProperty, value); }
    public bool IsIssue { get => (bool)GetValue(IsIssueProperty)!; set => SetValue(IsIssueProperty, value); }
    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty)!; set => SetValue(IsExpandedProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty)!; set => SetValue(IsSelectedProperty, value); }
    public ObservableCollection<DevToolsAutomationItem> Children => (ObservableCollection<DevToolsAutomationItem>)GetValue(ChildrenProperty)!;
    public ObservableCollection<DevToolsAutomationProperty> Properties => (ObservableCollection<DevToolsAutomationProperty>)GetValue(PropertiesProperty)!;
    public ObservableCollection<string> Patterns => (ObservableCollection<string>)GetValue(PatternsProperty)!;
    public string DisplayName => PeerName.Length > 0 ? PeerName : ClassName.Length > 0 ? ClassName : "Unnamed peer";
    public string AutomationName => DisplayName + (Role.Length > 0 ? " · " + Role : "") + (Note.Length > 0 ? " · " + Note : "");
    public event PropertyChangedEventHandler? PropertyChanged;
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(e.Property.Name));
        if (e.Property == PeerNameProperty || e.Property == ClassNameProperty) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        if (e.Property == PeerNameProperty || e.Property == ClassNameProperty || e.Property == RoleProperty || e.Property == NoteProperty)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutomationName)));
    }
    internal void AttachPeer(AutomationPeer peer) { _peer = new(peer); Identity = Identities.GetValue(peer, _ => new object()); }
    internal bool TryGetPeer(out AutomationPeer? peer) { peer = null; return _peer?.TryGetTarget(out peer) == true; }
    public override string ToString() => AutomationName;
}
