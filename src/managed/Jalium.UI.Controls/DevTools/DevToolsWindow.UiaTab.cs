using Jalium.UI.Automation;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.DevTools;

public partial class DevToolsWindow
{
    private TreeView? _uiaTreeView;
    private StackPanel? _uiaDetailsPanel;
    private readonly Queue<UiaBuildTask> _uiaPendingBuild = new();
    private DispatcherTimer? _uiaBuildTimer;
    private bool _uiaTreeBuilt;

    /// <summary>
    /// TreeView container carrying a reference back to its <see cref="AutomationPeer"/>
    /// so the tree selection and context-menu can map a visible row to its peer.
    /// </summary>
    private sealed class UiaTreeViewItem : TreeViewItem
    {
        public AutomationPeer Peer { get; }
        public UiaTreeViewItem(AutomationPeer peer)
        {
            Peer = peer;
            Header = DescribeUiaPeer(peer);
        }
    }

    private sealed class UiaBuildTask
    {
        public UiaBuildTask(UiaTreeViewItem item, AutomationPeer peer, int level)
        {
            Item = item;
            Peer = peer;
            Level = level;
        }
        public UiaTreeViewItem Item { get; }
        public AutomationPeer Peer { get; }
        public int Level { get; }
    }

    private UIElement BuildUiaTab()
    {
        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Toolbar
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(DevToolsUi.Button("Refresh", BuildUiaTree, DevToolsUi.ButtonStyle.Primary, icon: "↻"));
        toolbar.Children.Add(DevToolsUi.Muted("Click a peer to see its properties and supported patterns."));
        var toolbarBar = DevToolsUi.Toolbar(toolbar);
        Grid.SetRow(toolbarBar, 0);
        outer.Children.Add(toolbarBar);

        // Split: tree | details
        var grid = new Grid { Margin = new Thickness(DevToolsTheme.GutterBase) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _uiaTreeView = new TreeView
        {
            Background = DevToolsTheme.SurfaceAlt,
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = DevToolsTheme.ThicknessHairline,
            CornerRadius = DevToolsTheme.RadiusBase,
            Margin = new Thickness(0),
        };
        _uiaTreeView.SelectedItemChanged += OnUiaTreeSelectionChanged;
        Grid.SetColumn(_uiaTreeView, 0);
        grid.Children.Add(_uiaTreeView);

        var splitter = new GridSplitter
        {
            Background = DevToolsTheme.BorderSubtle,
            ResizeDirection = GridResizeDirection.Columns,
            Margin = new Thickness(DevToolsTheme.GutterXS, 0, DevToolsTheme.GutterXS, 0),
            Width = 2,
        };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        _uiaDetailsPanel = new StackPanel { Margin = new Thickness(DevToolsTheme.GutterLg) };
        _uiaDetailsPanel.Children.Add(DevToolsUi.Muted("Activate the tab to enumerate the AutomationPeer tree."));
        var right = new Border
        {
            Background = DevToolsTheme.SurfaceAlt,
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = DevToolsTheme.ThicknessHairline,
            CornerRadius = DevToolsTheme.RadiusBase,
            Child = new ScrollViewer
            {
                Content = _uiaDetailsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            ClipToBounds = true,
        };
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        Grid.SetRow(grid, 1);
        outer.Children.Add(grid);

        return new Border
        {
            Background = DevToolsTheme.Surface,
            Child = outer,
            ClipToBounds = true,
        };
    }

    partial void OnUiaTabActivated()
    {
        if (_uiaTreeBuilt) return;
        BuildUiaTree();
    }

    private void BuildUiaTree()
    {
        if (_uiaTreeView == null) return;
        _uiaTreeBuilt = true;
        _uiaTreeView.Items.Clear();
        _uiaPendingBuild.Clear();

        var rootPeer = _targetWindow.GetAutomationPeer();
        if (rootPeer == null)
        {
            // TreeView expects own-container TreeViewItems; render a leaf explanation
            // row so the empty state keeps the overall style consistent.
            var empty = new TreeViewItem { Header = "Window has no AutomationPeer (OnCreateAutomationPeer returned null)." };
            _uiaTreeView.Items.Add(empty);
            return;
        }

        // Build the root up-front just like the Inspector: attach metadata before
        // adding to the TreeView, then expand. Children are filled asynchronously
        // by a dispatcher timer so the VSP container pipeline stays stable.
        var root = new UiaTreeViewItem(rootPeer);
        root.ParentTreeView = _uiaTreeView;
        root.Level = 0;
        _uiaTreeView.Items.Add(root);
        root.IsExpanded = true;

        _uiaPendingBuild.Enqueue(new UiaBuildTask(root, rootPeer, 0));
        ScheduleUiaBuild();
    }

    private void ScheduleUiaBuild()
    {
        if (_uiaPendingBuild.Count == 0) return;
        if (_uiaBuildTimer == null)
        {
            _uiaBuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _uiaBuildTimer.Tick += OnUiaBuildTimerTick;
        }
        if (!_uiaBuildTimer.IsEnabled)
            _uiaBuildTimer.Start();
    }

    private const int UiaBuildNodeBatch = 8;
    private const int UiaBuildChildBatch = 48;

    private void OnUiaBuildTimerTick(object? sender, EventArgs e)
    {
        _uiaBuildTimer?.Stop();

        int processedNodes = 0;
        int processedChildren = 0;
        while (processedNodes < UiaBuildNodeBatch &&
               processedChildren < UiaBuildChildBatch &&
               _uiaPendingBuild.Count > 0)
        {
            var task = _uiaPendingBuild.Dequeue();

            List<AutomationPeer> children;
            try { children = task.Peer.GetChildren() ?? new List<AutomationPeer>(); }
            catch { children = new List<AutomationPeer>(); }

            var childItems = new List<TreeViewItem>();
            foreach (var child in children)
            {
                if (child == null) continue;
                var childItem = new UiaTreeViewItem(child);
                childItems.Add(childItem);
                _uiaPendingBuild.Enqueue(new UiaBuildTask(childItem, child, task.Level + 1));
                processedChildren++;
                if (processedChildren >= UiaBuildChildBatch) break;
            }

            if (childItems.Count > 0)
                task.Item.AddChildItems(childItems);

            processedNodes++;
        }

        if (_uiaPendingBuild.Count > 0) ScheduleUiaBuild();
    }

    private void OnUiaTreeSelectionChanged(object? sender, RoutedPropertyChangedEventArgs<object?> e)
    {
        if (e.NewValue is UiaTreeViewItem item)
            ShowUiaDetails(item.Peer);
    }

    private static string DescribeUiaPeer(AutomationPeer peer)
    {
        string name = SafeGet(() => peer.GetName()) ?? "";
        string role = SafeGet(() => peer.GetAutomationControlType().ToString()) ?? "?";
        string cls = SafeGet(() => peer.GetClassName()) ?? peer.GetType().Name;
        return string.IsNullOrEmpty(name) ? $"{cls}  ({role})" : $"{cls}  ({role})  \"{name}\"";
    }

    private static string? SafeGet(Func<string> fn)
    {
        try { return fn(); }
        catch { return null; }
    }

    private void ShowUiaDetails(AutomationPeer peer)
    {
        if (_uiaDetailsPanel == null) return;
        _uiaDetailsPanel.Children.Clear();

        void PropertyRow(string label, string value, Brush? valueBrush = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = DevToolsTheme.FontSm,
                FontFamily = DevToolsTheme.UiFont,
                Foreground = DevToolsTheme.TokenProperty,
                MinWidth = 140,
                VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = DevToolsTheme.FontSm,
                FontFamily = DevToolsTheme.MonoFont,
                Foreground = valueBrush ?? DevToolsTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            _uiaDetailsPanel.Children.Add(row);
        }

        _uiaDetailsPanel.Children.Add(DevToolsUi.SectionHeading("Properties"));
        try { PropertyRow("Name", string.IsNullOrEmpty(peer.GetName()) ? "(none)" : $"\"{peer.GetName()}\"", DevToolsTheme.TokenString); }
        catch (Exception ex) { PropertyRow("Name", $"<error: {ex.Message}>", DevToolsTheme.Error); }
        try { PropertyRow("ControlType", peer.GetAutomationControlType().ToString(), DevToolsTheme.TokenEnum); } catch { }
        try { PropertyRow("ClassName", peer.GetClassName()); } catch { }
        try { PropertyRow("IsEnabled", peer.IsEnabled().ToString(), DevToolsTheme.TokenBool); } catch { }
        try { PropertyRow("IsKeyboardFocusable", peer.IsKeyboardFocusable().ToString(), DevToolsTheme.TokenBool); } catch { }
        try { PropertyRow("HasKeyboardFocus", peer.HasKeyboardFocus().ToString(), DevToolsTheme.TokenBool); } catch { }
        try { PropertyRow("BoundingRectangle", peer.GetBoundingRectangle().ToString(), DevToolsTheme.TokenNumber); } catch { }

        _uiaDetailsPanel.Children.Add(DevToolsUi.SectionHeading("Supported patterns"));
        var patternsRow = new WrapPanel();
        int patternCount = 0;
        foreach (PatternInterface pat in Enum.GetValues<PatternInterface>())
        {
            object? impl = null;
            try { impl = peer.GetPattern(pat); } catch { }
            if (impl == null) continue;
            patternsRow.Children.Add(DevToolsUi.Pill(pat.ToString(), DevToolsTheme.Success));
            patternCount++;
        }
        if (patternCount == 0)
        {
            _uiaDetailsPanel.Children.Add(DevToolsUi.Muted("(no patterns exposed)"));
        }
        else
        {
            _uiaDetailsPanel.Children.Add(patternsRow);
        }
    }
}
