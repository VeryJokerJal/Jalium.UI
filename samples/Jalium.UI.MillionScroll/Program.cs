using System.Collections;
using System.Globalization;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Hosting;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using SelectionMode = Jalium.UI.Controls.SelectionMode;

namespace Jalium.UI.MillionScroll;

internal static class Program
{
    private const int DefaultRowCount = 1_000_000;

    [STAThread]
    private static int Main(string[] args)
    {
        var renderContext = RenderContext.GetOrCreateCurrent(RenderBackend.D3D12);
        renderContext.DefaultRenderingEngine = RenderingEngine.Impeller;

        var rowCount = ReadRowCount(args, DefaultRowCount);
        var builder = AppBuilder.CreateBuilder(args);
        builder.ConfigureApplication(app => app.MainWindow = MillionScrollWindow.Build(rowCount));

        using var host = builder.Build();
        host.UseDeveloperTools();
        return host.Run();
    }

    private static int ReadRowCount(string[] args, int fallback)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--rows=", StringComparison.OrdinalIgnoreCase))
            {
                return ClampRows(arg["--rows=".Length..], fallback);
            }

            if (string.Equals(arg, "--rows", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return ClampRows(args[i + 1], fallback);
            }
        }

        return fallback;
    }

    private static int ClampRows(string value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 1, 10_000_000)
            : fallback;
    }
}

internal sealed class MillionScrollWindow
{
    private const double RowVisualHeight = 30;
    private const double EstimatedContainerHeight = 42;

    private readonly ProbeListBox _listBox = new();
    private readonly NumberBox _rowCountBox = new();
    private readonly NumberBox _jumpBox = new();
    private readonly Slider _speedSlider = new();
    private readonly TextBlock _speedValueText = MetricValue("40 px/tick");
    private readonly TextBlock _rowCountText = MetricValue("");
    private readonly TextBlock _realizedText = MetricValue("");
    private readonly TextBlock _rangeText = MetricValue("");
    private readonly TextBlock _offsetText = MetricValue("");
    private readonly TextBlock _memoryText = MetricValue("");
    private readonly TextBlock _selectedText = MutedText("");
    private readonly DispatcherTimer _autoScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };

    private ScrollViewer? _scrollViewer;
    private Button? _autoButton;
    private int _rowCount;
    private bool _autoScrollEnabled;

    private MillionScrollWindow(int rowCount)
    {
        _rowCount = rowCount;
    }

    public static Window Build(int rowCount)
    {
        var controller = new MillionScrollWindow(rowCount);
        return controller.BuildWindow();
    }

    private Window BuildWindow()
    {
        var window = new Window
        {
            Title = "Jalium.UI Million Row Scroll Lab",
            Width = 1280,
            Height = 860,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Solid(18, 22, 24),
        };

        _listBox.ItemsSource = new MillionRowSource(_rowCount);
        _listBox.ItemTemplate = CreateRowTemplate();
        _listBox.SelectionMode = SelectionMode.Single;
        _listBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        _listBox.VerticalAlignment = VerticalAlignment.Stretch;
        _listBox.Background = Solid(18, 22, 24);
        _listBox.BorderThickness = new Thickness(0);
        _listBox.CornerRadius = new CornerRadius(0);
        _listBox.Loaded += (_, _) =>
        {
            _scrollViewer = FindDescendant<ScrollViewer>(_listBox);
            RefreshHud();
        };
        _listBox.SelectionChanged += (_, _) => UpdateSelectionText();

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

        var sidebar = BuildSidebar();
        Grid.SetColumn(sidebar, 0);
        root.Children.Add(sidebar);

        var workArea = BuildWorkArea();
        Grid.SetColumn(workArea, 1);
        root.Children.Add(workArea);

        window.Content = root;

        _autoScrollTimer.Tick += (_, _) => TickAutoScroll();
        _hudTimer.Tick += (_, _) => RefreshHud();
        _hudTimer.Start();

        return window;
    }

    private UIElement BuildSidebar()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 16,
            Margin = new Thickness(20),
        };

        stack.Children.Add(new TextBlock
        {
            Text = "Million Scroll",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = Solid(238, 241, 232),
        });

        stack.Children.Add(new TextBlock
        {
            Text = "D3D12 data run",
            FontSize = 13,
            Foreground = Solid(146, 158, 151),
        });

        _rowCountBox.Minimum = 1;
        _rowCountBox.Maximum = 10_000_000;
        _rowCountBox.Value = _rowCount;
        _rowCountBox.DecimalPlaces = 0;
        _rowCountBox.Height = 36;
        stack.Children.Add(Field("Rows", _rowCountBox));
        stack.Children.Add(ActionButton("Apply rows", ApplyRows));

        _jumpBox.Minimum = 1;
        _jumpBox.Maximum = _rowCount;
        _jumpBox.Value = Math.Min(_rowCount, 500_000);
        _jumpBox.DecimalPlaces = 0;
        _jumpBox.Height = 36;
        stack.Children.Add(Field("Jump to row", _jumpBox));
        stack.Children.Add(ActionButton("Jump", JumpToRow));

        var jumpRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        jumpRow.Children.Add(ActionButton("Top", () => JumpToIndex(0), width: 88));
        jumpRow.Children.Add(ActionButton("Half", () => JumpToIndex(_rowCount / 2), width: 88));
        jumpRow.Children.Add(ActionButton("Bottom", () => JumpToIndex(_rowCount - 1), width: 88));
        stack.Children.Add(jumpRow);

        _speedSlider.Minimum = 1;
        _speedSlider.Maximum = 240;
        _speedSlider.Value = 40;
        _speedSlider.Height = 34;
        _speedSlider.ValueChanged += (_, _) => UpdateSpeedText();

        var speedPanel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        speedPanel.Children.Add(InlineLabel("Auto speed", _speedValueText));
        speedPanel.Children.Add(_speedSlider);
        stack.Children.Add(speedPanel);

        _autoButton = ActionButton("Start auto scroll", ToggleAutoScroll);
        stack.Children.Add(_autoButton);

        stack.Children.Add(new Border
        {
            Height = 1,
            Background = Solid(54, 63, 63),
            Margin = new Thickness(0, 4, 0, 0),
        });

        stack.Children.Add(MetricBlock("Rows", _rowCountText));
        stack.Children.Add(MetricBlock("Realized", _realizedText));
        stack.Children.Add(MetricBlock("Range", _rangeText));
        stack.Children.Add(MetricBlock("Offset", _offsetText));
        stack.Children.Add(MetricBlock("Managed", _memoryText));
        stack.Children.Add(_selectedText);

        return new Border
        {
            Background = Solid(25, 31, 32),
            BorderBrush = Solid(51, 62, 61),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = stack,
        };
    }

    private UIElement BuildWorkArea()
    {
        var root = new DockPanel { LastChildFill = true };

        var header = new Grid
        {
            Background = Solid(19, 25, 26),
            ColumnSpacing = 16,
            Margin = new Thickness(0),
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            Margin = new Thickness(22, 14, 22, 12),
        };
        titleStack.Children.Add(new TextBlock
        {
            Text = "Data stream",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = Solid(238, 241, 232),
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "rows / shards / payloads",
            FontSize = 12,
            Foreground = Solid(144, 156, 150),
        });
        Grid.SetColumn(titleStack, 0);
        header.Children.Add(titleStack);

        var status = new Border
        {
            Background = Solid(33, 47, 45),
            BorderBrush = Solid(63, 91, 83),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 16, 22, 16),
            Child = new TextBlock
            {
                Text = "D3D12 / Impeller",
                FontSize = 12,
                Foreground = Solid(148, 225, 183),
            },
        };
        Grid.SetColumn(status, 1);
        header.Children.Add(status);

        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var listShell = new Border
        {
            Background = Solid(18, 22, 24),
            BorderBrush = Solid(42, 50, 50),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = _listBox,
        };
        root.Children.Add(listShell);
        return root;
    }

    private static DataTemplate CreateRowTemplate()
    {
        var template = new DataTemplate(typeof(MillionRow));
        template.SetVisualTree(() =>
        {
            var grid = new Grid
            {
                Height = RowVisualHeight,
                ColumnSpacing = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

            AddCell(grid, 0, nameof(MillionRow.IndexText), Solid(163, 213, 185), FontWeights.SemiBold);
            AddCell(grid, 1, nameof(MillionRow.Key), Solid(222, 228, 220), FontWeights.Normal);
            AddCell(grid, 2, nameof(MillionRow.Group), Solid(163, 185, 218), FontWeights.Normal);
            AddCell(grid, 3, nameof(MillionRow.ValueText), Solid(227, 186, 113), FontWeights.Normal);
            AddCell(grid, 4, nameof(MillionRow.Payload), Solid(142, 151, 145), FontWeights.Normal);

            return new Border
            {
                Height = RowVisualHeight,
                BorderBrush = Solid(36, 43, 43),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid,
            };
        });
        template.Seal();
        return template;
    }

    private static void AddCell(Grid grid, int column, string path, Brush foreground, FontWeight weight)
    {
        var text = new TextBlock
        {
            FontSize = 13,
            FontWeight = weight,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetBinding(TextBlock.TextProperty, new Binding(path));
        Grid.SetColumn(text, column);
        grid.Children.Add(text);
    }

    private void ApplyRows()
    {
        _rowCount = Math.Clamp((int)Math.Round(_rowCountBox.Value), 1, 10_000_000);
        _jumpBox.Maximum = _rowCount;
        _jumpBox.Value = Math.Clamp(_jumpBox.Value, 1, _rowCount);
        _listBox.ItemsSource = new MillionRowSource(_rowCount);
        GetScrollViewer()?.ScrollToVerticalOffset(0);
        RefreshHud();
    }

    private void JumpToRow()
    {
        JumpToIndex((int)Math.Round(_jumpBox.Value) - 1);
    }

    private void JumpToIndex(int index)
    {
        if (_rowCount <= 0)
        {
            return;
        }

        var targetIndex = Math.Clamp(index, 0, _rowCount - 1);
        var scroller = GetScrollViewer();
        if (scroller == null)
        {
            return;
        }

        var targetOffset = targetIndex * EstimatedContainerHeight;
        scroller.ScrollToVerticalOffset(Math.Clamp(targetOffset, 0, scroller.ScrollableHeight));
        _jumpBox.Value = targetIndex + 1;
        RefreshHud();
    }

    private void ToggleAutoScroll()
    {
        _autoScrollEnabled = !_autoScrollEnabled;
        if (_autoScrollEnabled)
        {
            _autoScrollTimer.Start();
            SetButtonText(_autoButton, "Stop auto scroll");
        }
        else
        {
            _autoScrollTimer.Stop();
            SetButtonText(_autoButton, "Start auto scroll");
        }
    }

    private void TickAutoScroll()
    {
        var scroller = GetScrollViewer();
        if (scroller == null)
        {
            return;
        }

        var next = scroller.VerticalOffset + Math.Max(1, _speedSlider.Value);
        scroller.ScrollToVerticalOffset(next >= scroller.ScrollableHeight ? 0 : next);
    }

    private void RefreshHud()
    {
        var scroller = GetScrollViewer();
        var host = _listBox.Host;
        var realized = host?.Children.Count ?? 0;
        var first = int.MaxValue;
        var last = -1;

        if (host != null)
        {
            foreach (var child in host.Children)
            {
                if (child is DependencyObject dependencyObject)
                {
                    var index = _listBox.ItemContainerGenerator.IndexFromContainer(dependencyObject);
                    if (index >= 0)
                    {
                        first = Math.Min(first, index);
                        last = Math.Max(last, index);
                    }
                }
            }
        }

        _rowCountText.Text = FormatInt(_rowCount);
        _realizedText.Text = realized.ToString(CultureInfo.InvariantCulture);
        _rangeText.Text = last >= 0
            ? $"{FormatInt(first + 1)} - {FormatInt(last + 1)}"
            : "-";

        _offsetText.Text = scroller == null
            ? "-"
            : $"{scroller.VerticalOffset:0} / {scroller.ScrollableHeight:0}";

        _memoryText.Text = $"{GC.GetTotalMemory(false) / 1024.0 / 1024.0:0.0} MB";
        UpdateSelectionText();
    }

    private void UpdateSelectionText()
    {
        _selectedText.Text = _listBox.SelectedItem is MillionRow row
            ? $"Selected row {FormatInt(row.RowNumber)}"
            : "Selected row -";
    }

    private void UpdateSpeedText()
    {
        _speedValueText.Text = $"{Math.Round(_speedSlider.Value):0} px/tick";
    }

    private ScrollViewer? GetScrollViewer()
    {
        _scrollViewer ??= FindDescendant<ScrollViewer>(_listBox);
        return _scrollViewer;
    }

    private static T? FindDescendant<T>(Visual root) where T : Visual
    {
        for (var i = 0; i < root.VisualChildrenCount; i++)
        {
            var child = root.GetVisualChild(i);
            if (child is T match)
            {
                return match;
            }

            if (child != null)
            {
                var descendant = FindDescendant<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }
        }

        return null;
    }

    private static UIElement Field(string label, FrameworkElement editor)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        stack.Children.Add(Label(label));
        stack.Children.Add(editor);
        return stack;
    }

    private static UIElement InlineLabel(string label, TextBlock value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelText = Label(label);
        Grid.SetColumn(labelText, 0);
        grid.Children.Add(labelText);

        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return grid;
    }

    private static UIElement MetricBlock(string label, TextBlock value)
    {
        return new Border
        {
            Background = Solid(20, 25, 26),
            BorderBrush = Solid(47, 57, 57),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 9, 12, 9),
            Child = InlineLabel(label, value),
        };
    }

    private static TextBlock Label(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = Solid(137, 149, 143),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static TextBlock MetricValue(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Solid(230, 235, 226),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static TextBlock MutedText(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = Solid(137, 149, 143),
        };
    }

    private static Button ActionButton(string text, Action action, double width = double.NaN)
    {
        var button = new Button
        {
            Background = Solid(34, 51, 49),
            BorderBrush = Solid(64, 96, 87),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            MinHeight = 36,
            Padding = new Thickness(12, 7, 12, 7),
            Content = new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Solid(228, 239, 225),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        if (!double.IsNaN(width))
        {
            button.Width = width;
        }

        button.Click += (_, _) => action();
        return button;
    }

    private static void SetButtonText(Button? button, string text)
    {
        if (button?.Content is TextBlock textBlock)
        {
            textBlock.Text = text;
        }
    }

    private static string FormatInt(int value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static SolidColorBrush Solid(byte r, byte g, byte b)
    {
        return new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    private sealed class ProbeListBox : ListBox
    {
        public Panel? Host => ItemsHost;
    }
}

internal readonly record struct MillionRow(int Index)
{
    public int RowNumber => Index + 1;

    public string IndexText => RowNumber.ToString("N0", CultureInfo.InvariantCulture);

    public string Key => FormattableString.Invariant($"ROW-{Index % 10_000:0000}-{Index:X6}");

    public string Group => FormattableString.Invariant($"Shard {Index % 64:00}");

    public string ValueText => ((Index * 2_654_435_761L) & 0xffff).ToString("X4", CultureInfo.InvariantCulture);

    public string Payload => string.Format(
        CultureInfo.InvariantCulture,
        "segment={0:00} bucket={1:000} sample={2:000000}",
        (Index / 1000) % 80,
        (Index * 17) % 997,
        (Index * 37) % 1_000_000);
}

internal sealed class MillionRowSource : IList
{
    public MillionRowSource(int count)
    {
        Count = Math.Clamp(count, 1, 10_000_000);
    }

    public int Count { get; }

    public bool IsFixedSize => true;

    public bool IsReadOnly => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    public object? this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return new MillionRow(index);
        }
        set => throw new NotSupportedException();
    }

    public int Add(object? value) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public bool Contains(object? value)
    {
        return IndexOf(value) >= 0;
    }

    public int IndexOf(object? value)
    {
        return value is MillionRow row && row.Index >= 0 && row.Index < Count
            ? row.Index
            : -1;
    }

    public void Insert(int index, object? value) => throw new NotSupportedException();

    public void Remove(object? value) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();

    public void CopyTo(Array array, int index)
    {
        for (var i = 0; i < Count; i++)
        {
            array.SetValue(new MillionRow(i), index + i);
        }
    }

    public IEnumerator GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return new MillionRow(i);
        }
    }
}
