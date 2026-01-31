using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Gallery.Theme;
using Jalium.UI.Gallery.Views;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

/// <summary>
/// Main window for the Jalium.UI Gallery application.
/// NavigationView is defined in GalleryWindow.jalxaml and wired up by source generator.
/// </summary>
public partial class GalleryWindow : Window
{
    private readonly Dictionary<NavigationViewItem, string> _itemTags = new();

    // All pages use code-behind (x:Class + .jalxaml.cs)
    // Category pages and HomePage are handled specially to wire up navigation events
    private static readonly Dictionary<string, Func<UIElement>> Pages = new()
    {
        // These are placeholders - created in NavigateToPage with event wiring
        { "home", () => null! },
        { "basic", () => null! },
        { "text", () => null! },
        { "layout", () => null! },
        { "navigation", () => null! },
        { "media", () => null! },
        { "collections", () => null! },
        // Static pages
        { "getting-started", () => new GettingStartedPage() },
        // Individual control pages
        { "button", () => new ButtonPage() },
        { "checkbox", () => new CheckBoxPage() },
        { "radiobutton", () => new RadioButtonPage() },
        { "slider", () => new SliderPage() },
        { "progressbar", () => new ProgressBarPage() },
        { "textbox", () => new TextBoxPage() },
        { "passwordbox", () => new PasswordBoxPage() },
        { "combobox", () => new ComboBoxPage() },
        { "textblock", () => new TextBlockPage() },
        { "binding", () => new BindingPage() },
        { "stackpanel", () => new StackPanelPage() },
        { "grid", () => new GridPage() },
        { "canvas", () => new CanvasPage() },
        { "border", () => new BorderPage() },
        { "dockpanel", () => new DockPanelPage() },
        { "wrappanel", () => new WrapPanelPage() },
        { "scrollviewer", () => new ScrollViewerPage() },
        { "tabcontrol", () => new TabControlPage() },
        { "treeview", () => new TreeViewPage() },
        { "image", () => new ImagePage() },
        { "listbox", () => new ListBoxPage() },
        { "backdropeffects", () => new BackdropEffectsPage() },
        { "datagrid", () => new DataGridPage() },
        { "autocompletebox", () => new AutoCompleteBoxPage() },
        //{ "datepicker", () => new DatePickerPage() },
        //{ "colorpicker", () => new ColorPickerPage() },
        //{ "expander", () => new ExpanderPage() },
        { "groupbox", () => new GroupBoxPage() },
        { "statusbar", () => new StatusBarPage() },
        { "label", () => new LabelPage() },
        { "separator", () => new SeparatorPage() },
        { "hyperlinkbutton", () => new HyperlinkButtonPage() },
        { "toggleswitch", () => new ToggleSwitchPage() },
        { "numberbox", () => new NumberBoxPage() },
        { "repeatbutton", () => new RepeatButtonPage() },
        { "thumb", () => new ThumbPage() },
        { "calendar", () => new CalendarPage() },
        { "infobar", () => new InfoBarPage() }
    };

    public GalleryWindow()
    {
        InitializeComponent();

        // Add navigation items
        AddNavigationItems();

        // Handle selection changed
        if (NavigationView != null)
        {
            NavigationView.SelectionChanged += OnSelectionChanged;
        }

        // Navigate to home page
        NavigateToPage("home");
    }

    private void AddNavigationItems()
    {
        if (NavigationView == null) return;

        // Home
        AddItem("Home", "home");
        AddItem("Getting Started", "getting-started");

        // Controls (expandable group) - clicking group navigates to category overview
        var controlsGroup = AddGroupItem("Basic", "basic");
        AddChildItem(controlsGroup, "Button", "button");
        AddChildItem(controlsGroup, "CheckBox", "checkbox");
        AddChildItem(controlsGroup, "RadioButton", "radiobutton");
        AddChildItem(controlsGroup, "Slider", "slider");
        AddChildItem(controlsGroup, "ProgressBar", "progressbar");
        AddChildItem(controlsGroup, "TextBox", "textbox");
        AddChildItem(controlsGroup, "PasswordBox", "passwordbox");
        AddChildItem(controlsGroup, "ComboBox", "combobox");
        AddChildItem(controlsGroup, "AutoCompleteBox", "autocompletebox");
        AddChildItem(controlsGroup, "ToggleSwitch", "toggleswitch");
        AddChildItem(controlsGroup, "NumberBox", "numberbox");
        AddChildItem(controlsGroup, "RepeatButton", "repeatbutton");
        AddChildItem(controlsGroup, "HyperlinkButton", "hyperlinkbutton");

        // Text (expandable group) - clicking group navigates to category overview
        var textGroup = AddGroupItem("Text", "text");
        AddChildItem(textGroup, "TextBlock", "textblock");
        AddChildItem(textGroup, "Label", "label");

        // Data (expandable group) - data binding features
        var dataGroup = AddGroupItem("Data");
        AddChildItem(dataGroup, "Binding", "binding");

        // Layout (expandable group) - clicking group navigates to category overview
        var layoutGroup = AddGroupItem("Layout", "layout");
        AddChildItem(layoutGroup, "StackPanel", "stackpanel");
        AddChildItem(layoutGroup, "Grid", "grid");
        AddChildItem(layoutGroup, "Canvas", "canvas");
        AddChildItem(layoutGroup, "Border", "border");
        AddChildItem(layoutGroup, "DockPanel", "dockpanel");
        AddChildItem(layoutGroup, "WrapPanel", "wrappanel");
        AddChildItem(layoutGroup, "ScrollViewer", "scrollviewer");
        AddChildItem(layoutGroup, "Expander", "expander");
        AddChildItem(layoutGroup, "GroupBox", "groupbox");
        AddChildItem(layoutGroup, "Separator", "separator");

        // Navigation (expandable group) - clicking group navigates to category overview
        var navigationGroup = AddGroupItem("Navigation", "navigation");
        AddChildItem(navigationGroup, "TabControl", "tabcontrol");

        // Media (expandable group) - clicking group navigates to category overview
        var mediaGroup = AddGroupItem("Media", "media");
        AddChildItem(mediaGroup, "Image", "image");

        // Collections (expandable group) - clicking group navigates to category overview
        var collectionsGroup = AddGroupItem("Collections", "collections");
        AddChildItem(collectionsGroup, "ListBox", "listbox");
        AddChildItem(collectionsGroup, "TreeView", "treeview");
        AddChildItem(collectionsGroup, "DataGrid", "datagrid");
        AddChildItem(collectionsGroup, "Calendar", "calendar");

        // Date & Time (expandable group)
        var dateTimeGroup = AddGroupItem("Date & Time");
        AddChildItem(dateTimeGroup, "DatePicker", "datepicker");
        AddChildItem(dateTimeGroup, "Calendar", "calendar");

        // Pickers (expandable group)
        var pickersGroup = AddGroupItem("Pickers");
        AddChildItem(pickersGroup, "ColorPicker", "colorpicker");

        // Status & Info (expandable group)
        var statusGroup = AddGroupItem("Status & Info");
        AddChildItem(statusGroup, "StatusBar", "statusbar");
        AddChildItem(statusGroup, "InfoBar", "infobar");
        AddChildItem(statusGroup, "Thumb", "thumb");

        // Effects (expandable group)
        var effectsGroup = AddGroupItem("Effects");
        AddChildItem(effectsGroup, "Backdrop Effects", "backdropeffects");

        // Update the visual tree
        NavigationView.UpdateMenuItems();
    }

    private void AddItem(string name, string tag)
    {
        if (NavigationView == null) return;

        var item = new NavigationViewItem
        {
            Content = name,
            Height = 36
        };

        _itemTags[item] = tag;
        NavigationView.MenuItems.Add(item);
    }

    private NavigationViewItem AddGroupItem(string name, string? defaultPageTag = null)
    {
        var item = new NavigationViewItem
        {
            Content = name,
            Height = 36,
            SelectsOnInvoked = defaultPageTag != null, // Allow selection if we have a default page
            IsExpanded = true
        };

        if (defaultPageTag != null)
        {
            _itemTags[item] = defaultPageTag;
        }

        NavigationView?.MenuItems.Add(item);
        return item;
    }

    private void AddChildItem(NavigationViewItem parent, string name, string tag)
    {
        var item = new NavigationViewItem
        {
            Content = name,
            Height = 36
        };

        _itemTags[item] = tag;
        parent.MenuItems.Add(item);
    }

    private void OnSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem != null && _itemTags.TryGetValue(e.SelectedItem, out var tag))
        {
            NavigateToPage(tag);
        }
    }

    private void OnPageNavigationRequested(object? sender, NavigationRequestEventArgs e)
    {
        NavigateToPage(e.PageTag);

        // Also select the corresponding item in the navigation view
        SelectNavigationItem(e.PageTag);
    }

    private void SelectNavigationItem(string pageTag)
    {
        if (NavigationView == null) return;

        // Find the navigation item with the matching tag
        foreach (var kvp in _itemTags)
        {
            if (kvp.Value == pageTag)
            {
                NavigationView.SelectedItem = kvp.Key;
                break;
            }
        }
    }

    private void NavigateToPage(string pageTag)
    {
        if (NavigationView == null) return;

        UIElement? pageContent = null;

        // Special handling for pages that need navigation event wiring
        if (pageTag == "home")
        {
            var homePage = new HomePage();
            homePage.NavigationRequested += OnPageNavigationRequested;
            pageContent = homePage;
        }
        else if (pageTag == "basic")
        {
            var categoryPage = new BasicCategoryPage();
            categoryPage.NavigationRequested += OnPageNavigationRequested;
            pageContent = categoryPage;
        }
        else if (pageTag == "text")
        {
            var categoryPage = new TextCategoryPage();
            categoryPage.NavigationRequested += OnPageNavigationRequested;
            pageContent = categoryPage;
        }
        else if (pageTag == "layout")
        {
            var categoryPage = new LayoutCategoryPage();
            categoryPage.NavigationRequested += OnPageNavigationRequested;
            pageContent = categoryPage;
        }
        else if (pageTag == "navigation")
        {
            var categoryPage = new NavigationCategoryPage();
            categoryPage.NavigationRequested += OnPageNavigationRequested;
            pageContent = categoryPage;
        }
        else if (pageTag == "media")
        {
            var categoryPage = new MediaCategoryPage();
            categoryPage.NavigationRequested += OnPageNavigationRequested;
            pageContent = categoryPage;
        }
        else if (pageTag == "collections")
        {
            var categoryPage = new CollectionsCategoryPage();
            categoryPage.NavigationRequested += OnPageNavigationRequested;
            pageContent = categoryPage;
        }
        else if (Pages.TryGetValue(pageTag, out var factory))
        {
            try
            {
                pageContent = factory();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create page '{pageTag}': {ex.Message}");
            }
        }

        if (pageContent != null)
        {
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(32, 24, 32, 24),
                Background = GalleryTheme.BackgroundDarkBrush,
                ClipToBounds = false
            };
            scrollViewer.Content = pageContent;

            NavigationView.SetContent(scrollViewer);
        }
        else
        {
            var placeholder = CreatePlaceholderPage(pageTag);
            NavigationView.SetContent(placeholder);
        }
    }

    private UIElement CreatePlaceholderPage(string pageTag)
    {
        var container = new Border
        {
            Background = GalleryTheme.BackgroundDarkBrush,
            Padding = new Thickness(32)
        };

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var iconBorder = new Border
        {
            Width = 80,
            Height = 80,
            Background = GalleryTheme.BackgroundLightBrush,
            CornerRadius = new CornerRadius(40),
            Margin = new Thickness(0, 0, 0, 24),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var iconText = new TextBlock
        {
            Text = "?",
            FontSize = 32,
            Foreground = GalleryTheme.TextSecondaryBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconBorder.Child = iconText;
        stack.Children.Add(iconBorder);

        var title = new TextBlock
        {
            Text = $"Page: {pageTag}",
            FontSize = GalleryTheme.FontSizeSubtitle,
            FontWeight = FontWeights.SemiBold,
            Foreground = GalleryTheme.TextPrimaryBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        stack.Children.Add(title);

        var description = new TextBlock
        {
            Text = "This page is coming soon.",
            FontSize = GalleryTheme.FontSizeNormal,
            Foreground = GalleryTheme.TextSecondaryBrush,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        stack.Children.Add(description);

        container.Child = stack;
        return container;
    }
}
