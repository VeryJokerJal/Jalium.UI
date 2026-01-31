using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

#if DEBUG
/// <summary>
/// Developer tools window for inspecting the visual tree and element properties.
/// Similar to browser DevTools or Avalonia DevTools.
/// </summary>
public class DevToolsWindow : Window
{
    private readonly Window _targetWindow;
    private readonly Grid _mainGrid;
    private readonly TreeView _visualTreeView;
    private readonly StackPanel _propertiesPanel;
    private readonly ScrollViewer _propertiesScrollViewer;
    private Visual? _selectedVisual;
    private DevToolsOverlay? _overlay;

    /// <summary>
    /// DevToolsWindow cannot open its own DevTools.
    /// </summary>
    protected override bool CanOpenDevTools => false;

    /// <summary>
    /// Creates a new DevToolsWindow for inspecting the specified target window.
    /// </summary>
    /// <param name="targetWindow">The window to inspect.</param>
    public DevToolsWindow(Window targetWindow)
    {
        _targetWindow = targetWindow ?? throw new ArgumentNullException(nameof(targetWindow));

        Title = $"DevTools - {targetWindow.Title}";
        Width = 600;
        Height = 700;
        SystemBackdrop = WindowBackdropType.Mica;
        Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32));

        // Create main grid with two columns
        _mainGrid = new Grid();
        _mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left panel: Visual Tree
        var (leftPanel, treeView) = CreateVisualTreePanel();
        Grid.SetColumn(leftPanel, 0);
        _mainGrid.Children.Add(leftPanel);

        // Right panel: Properties
        var (rightPanel, scrollViewer, propertiesPanel) = CreatePropertiesPanel();
        Grid.SetColumn(rightPanel, 1);
        _mainGrid.Children.Add(rightPanel);

        Content = _mainGrid;

        // Store references
        _visualTreeView = treeView;
        _propertiesScrollViewer = scrollViewer;
        _propertiesPanel = propertiesPanel;

        // Build the visual tree
        RefreshVisualTree();

        // Create overlay on target window and set it on the target window
        _overlay = new DevToolsOverlay(_targetWindow);
        _targetWindow.DevToolsOverlay = _overlay;

        // Register keyboard event handler
        AddHandler(KeyDownEvent, new RoutedEventHandler(OnKeyDownHandler));

        // Handle window closing to ensure proper cleanup
        Closing += OnDevToolsClosing;
    }

    private bool _isClosing;

    private void OnDevToolsClosing(object? sender, EventArgs e)
    {
        // Only cleanup overlay - don't touch anything else
        // The window destruction will handle the rest
        _targetWindow.DevToolsOverlay = null;
        _overlay = null;
    }

    private void OnKeyDownHandler(object sender, RoutedEventArgs e)
    {
        if (e is KeyEventArgs keyArgs)
        {
            // F5 to refresh
            if (keyArgs.Key == Key.F5)
            {
                RefreshVisualTree();
                keyArgs.Handled = true;
            }
            // Escape or F12 to close
            else if (keyArgs.Key == Key.Escape || keyArgs.Key == Key.F12)
            {
                CloseDevTools();
                keyArgs.Handled = true;
            }
        }
    }

    private (Border border, TreeView treeView) CreateVisualTreePanel()
    {
        var treeView = new TreeView
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            Margin = new Thickness(4)
        };

        treeView.SelectedItemChanged += OnVisualTreeSelectionChanged;

        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = treeView
        };

        return (border, treeView);
    }

    private (Border border, ScrollViewer scrollViewer, StackPanel propertiesPanel) CreatePropertiesPanel()
    {
        var propertiesPanel = new StackPanel
        {
            Margin = new Thickness(4)
        };

        var scrollViewer = new ScrollViewer
        {
            Content = propertiesPanel,
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            ClipToBounds = false
        };

        var border = new Border
        {
            Child = scrollViewer
        };

        return (border, scrollViewer, propertiesPanel);
    }

    private void RefreshVisualTree()
    {
        _visualTreeView.Items.Clear();

        var rootItem = CreateTreeViewItem(_targetWindow);
        _visualTreeView.Items.Add(rootItem);
        rootItem.IsExpanded = true;
    }

    private DevToolsTreeViewItem CreateTreeViewItem(Visual visual)
    {
        var item = new DevToolsTreeViewItem(visual);

        // Add children
        for (int i = 0; i < visual.VisualChildrenCount; i++)
        {
            var child = visual.GetVisualChild(i);
            if (child != null)
            {
                var childItem = CreateTreeViewItem(child);
                item.Items.Add(childItem);
            }
        }

        return item;
    }

    private void OnVisualTreeSelectionChanged(object? sender, RoutedPropertyChangedEventArgs<object?> e)
    {
        if (e.NewValue is DevToolsTreeViewItem treeItem)
        {
            _selectedVisual = treeItem.Visual;
            UpdatePropertiesPanel(_selectedVisual);
            _overlay?.HighlightElement(_selectedVisual as UIElement);
        }
    }

    private void UpdatePropertiesPanel(Visual? visual)
    {
        _propertiesPanel.Children.Clear();

        if (visual == null)
        {
            return;
        }

        // Type header
        AddPropertyHeader($"Type: {visual.GetType().Name}");

        // Common properties for UIElement
        if (visual is UIElement uiElement)
        {
            AddPropertySection("Layout");
            AddProperty("DesiredSize", $"{uiElement.DesiredSize.Width:F1} x {uiElement.DesiredSize.Height:F1}");
            AddProperty("VisualBounds", $"{uiElement.VisualBounds.X:F1}, {uiElement.VisualBounds.Y:F1}, {uiElement.VisualBounds.Width:F1} x {uiElement.VisualBounds.Height:F1}");
            AddProperty("Visibility", uiElement.Visibility.ToString());
            AddProperty("IsEnabled", uiElement.IsEnabled.ToString());
            AddProperty("Opacity", uiElement.Opacity.ToString("F2"));

            if (uiElement is FrameworkElement fe)
            {
                AddPropertySection("FrameworkElement");
                AddProperty("ActualWidth", fe.ActualWidth.ToString("F1"));
                AddProperty("ActualHeight", fe.ActualHeight.ToString("F1"));
                AddProperty("Width", fe.Width.ToString("F1"));
                AddProperty("Height", fe.Height.ToString("F1"));
                AddProperty("MinWidth", fe.MinWidth.ToString("F1"));
                AddProperty("MinHeight", fe.MinHeight.ToString("F1"));
                AddProperty("MaxWidth", fe.MaxWidth.ToString("F1"));
                AddProperty("MaxHeight", fe.MaxHeight.ToString("F1"));
                AddProperty("Margin", fe.Margin.ToString());
                AddProperty("HorizontalAlignment", fe.HorizontalAlignment.ToString());
                AddProperty("VerticalAlignment", fe.VerticalAlignment.ToString());

                if (fe is Control control)
                {
                    AddPropertySection("Control");
                    AddProperty("Padding", control.Padding.ToString());
                    AddProperty("Background", GetBrushDescription(control.Background));
                    AddProperty("Foreground", GetBrushDescription(control.Foreground));
                    AddProperty("BorderBrush", GetBrushDescription(control.BorderBrush));
                    AddProperty("BorderThickness", control.BorderThickness.ToString());
                    AddProperty("FontSize", control.FontSize.ToString("F1"));
                    AddProperty("FontFamily", control.FontFamily ?? "(default)");
                }

                if (fe is ContentControl contentControl)
                {
                    AddPropertySection("ContentControl");
                    AddProperty("Content", contentControl.Content?.ToString() ?? "(null)");
                }

                if (fe is TextBlock textBlock)
                {
                    AddPropertySection("TextBlock");
                    AddProperty("Text", textBlock.Text ?? "(null)");
                    AddProperty("TextWrapping", textBlock.TextWrapping.ToString());
                }
            }
        }

        // Focus info
        if (visual is IInputElement inputElement)
        {
            AddPropertySection("Focus");
            AddProperty("Focusable", inputElement.Focusable.ToString());
            AddProperty("IsKeyboardFocused", (visual as UIElement)?.IsKeyboardFocused.ToString() ?? "N/A");
        }
    }

    private void AddPropertyHeader(string text)
    {
        var header = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(86, 156, 214)),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(4, 8, 4, 4)
        };
        _propertiesPanel.Children.Add(header);
    }

    private void AddPropertySection(string sectionName)
    {
        var section = new TextBlock
        {
            Text = sectionName,
            Foreground = new SolidColorBrush(Color.FromRgb(78, 201, 176)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 12, 4, 2)
        };
        _propertiesPanel.Children.Add(section);
    }

    private void AddProperty(string name, string value)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 2, 4, 2)
        };

        var nameText = new TextBlock
        {
            Text = $"{name}: ",
            Foreground = new SolidColorBrush(Color.FromRgb(156, 220, 254)),
            FontSize = 12,
            Width = 120
        };

        var valueText = new TextBlock
        {
            Text = value,
            Foreground = new SolidColorBrush(Color.FromRgb(206, 145, 120)),
            FontSize = 12
        };

        panel.Children.Add(nameText);
        panel.Children.Add(valueText);
        _propertiesPanel.Children.Add(panel);
    }

    private static string GetBrushDescription(Brush? brush)
    {
        return brush switch
        {
            SolidColorBrush scb => $"#{scb.Color.A:X2}{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}",
            null => "(null)",
            _ => brush.GetType().Name
        };
    }

    /// <summary>
    /// Closes the DevTools window and removes the overlay.
    /// </summary>
    public new void CloseDevTools()
    {
        if (_isClosing) return;
        _isClosing = true;

        // Remove overlay reference before closing
        _targetWindow.DevToolsOverlay = null;
        _overlay = null;

        // Close the window - this will trigger OnDevToolsClosing
        Close();
    }
}

/// <summary>
/// Custom TreeViewItem for displaying visual tree nodes.
/// </summary>
internal class DevToolsTreeViewItem : TreeViewItem
{
    public Visual Visual { get; }

    public DevToolsTreeViewItem(Visual visual)
    {
        Visual = visual;
        Header = GetVisualDisplayName(visual);
        Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220));
    }

    private static string GetVisualDisplayName(Visual visual)
    {
        var typeName = visual.GetType().Name;

        // Add additional info for common types
        if (visual is Window window)
        {
            return $"{typeName} \"{window.Title}\"";
        }
        if (visual is TextBlock textBlock && !string.IsNullOrEmpty(textBlock.Text))
        {
            var text = textBlock.Text.Length > 20 ? textBlock.Text[..20] + "..." : textBlock.Text;
            return $"{typeName} \"{text}\"";
        }
        if (visual is ContentControl { Content: string contentString })
        {
            var text = contentString.Length > 20 ? contentString[..20] + "..." : contentString;
            return $"{typeName} \"{text}\"";
        }
        if (visual is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
        {
            return $"{typeName} #{fe.Name}";
        }

        return typeName;
    }
}
#endif
