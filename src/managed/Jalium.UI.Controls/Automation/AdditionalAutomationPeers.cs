using Jalium.UI.Automation;
using Jalium.UI.Controls.Navigation;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls.Automation;

#region Expander / GroupBox

/// <summary>
/// Exposes Expander types to UI Automation.
/// </summary>
public sealed class ExpanderAutomationPeer : FrameworkElementAutomationPeer, IExpandCollapseProvider
{
    public ExpanderAutomationPeer(Expander owner) : base(owner) { }

    private Expander ExpanderOwner => (Expander)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(Expander);

    protected override string GetNameCore()
    {
        var header = ExpanderOwner.Header;
        if (header is string text) return text;
        return header?.ToString() ?? base.GetNameCore();
    }

    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.ExpandCollapse)
            return this;
        return base.GetPatternCore(patternInterface);
    }

    public ExpandCollapseState ExpandCollapseState =>
        ExpanderOwner.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand()
    {
        if (!IsEnabled()) throw new InvalidOperationException("Cannot expand a disabled Expander.");
        ExpanderOwner.IsExpanded = true;
    }

    public void Collapse()
    {
        if (!IsEnabled()) throw new InvalidOperationException("Cannot collapse a disabled Expander.");
        ExpanderOwner.IsExpanded = false;
    }
}

/// <summary>
/// Exposes GroupBox types to UI Automation.
/// </summary>
public sealed class GroupBoxAutomationPeer : FrameworkElementAutomationPeer
{
    public GroupBoxAutomationPeer(GroupBox owner) : base(owner) { }

    private GroupBox GroupBoxOwner => (GroupBox)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(GroupBox);

    protected override string GetNameCore()
    {
        var header = GroupBoxOwner.Header;
        if (header is string text) return text;
        return header?.ToString() ?? base.GetNameCore();
    }
}

#endregion

#region Image / TextBlock / ToolTip

/// <summary>
/// Exposes Image types to UI Automation.
/// </summary>
public sealed class ImageAutomationPeer : FrameworkElementAutomationPeer
{
    public ImageAutomationPeer(Image owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Image;

    protected override string GetClassNameCore() => nameof(Image);
}

/// <summary>
/// Exposes TextBlock types to UI Automation.
/// </summary>
public sealed class TextBlockAutomationPeer : FrameworkElementAutomationPeer
{
    public TextBlockAutomationPeer(TextBlock owner) : base(owner) { }

    private TextBlock TextBlockOwner => (TextBlock)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Text;

    protected override string GetClassNameCore() => nameof(TextBlock);

    protected override string GetNameCore()
        => TextBlockOwner.Text ?? base.GetNameCore();
}

/// <summary>
/// Exposes ToolTip types to UI Automation.
/// </summary>
public sealed class ToolTipAutomationPeer : FrameworkElementAutomationPeer
{
    public ToolTipAutomationPeer(ToolTip owner) : base(owner) { }

    private ToolTip ToolTipOwner => (ToolTip)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.ToolTip;

    protected override string GetClassNameCore() => nameof(ToolTip);

    protected override string GetNameCore()
    {
        var content = ToolTipOwner.Content;
        if (content is string text) return text;
        return content?.ToString() ?? base.GetNameCore();
    }
}

#endregion

#region HyperlinkButton / RepeatButton / Thumb / GridSplitter

/// <summary>
/// Exposes HyperlinkButton types to UI Automation.
/// </summary>
public sealed class HyperlinkButtonAutomationPeer : ButtonBaseAutomationPeer
{
    public HyperlinkButtonAutomationPeer(HyperlinkButton owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Hyperlink;

    protected override string GetClassNameCore() => nameof(HyperlinkButton);
}

/// <summary>
/// Exposes RepeatButton types to UI Automation.
/// </summary>
public sealed class RepeatButtonAutomationPeer : ButtonBaseAutomationPeer
{
    public RepeatButtonAutomationPeer(RepeatButton owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Button;

    protected override string GetClassNameCore() => nameof(RepeatButton);
}

/// <summary>
/// Exposes Thumb types to UI Automation.
/// </summary>
public sealed class ThumbAutomationPeer : FrameworkElementAutomationPeer
{
    public ThumbAutomationPeer(Thumb owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Thumb;

    protected override string GetClassNameCore() => nameof(Thumb);
}

/// <summary>
/// Exposes GridSplitter types to UI Automation.
/// </summary>
public sealed class GridSplitterAutomationPeer : FrameworkElementAutomationPeer
{
    public GridSplitterAutomationPeer(GridSplitter owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Thumb;

    protected override string GetClassNameCore() => nameof(GridSplitter);
}

#endregion

#region RichTextBox

/// <summary>
/// Exposes RichTextBox types to UI Automation.
/// </summary>
public sealed class RichTextBoxAutomationPeer : FrameworkElementAutomationPeer
{
    public RichTextBoxAutomationPeer(RichTextBox owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Document;

    protected override string GetClassNameCore() => nameof(RichTextBox);
}

#endregion

#region ListView

/// <summary>
/// Exposes ListView types to UI Automation.
/// </summary>
public sealed class ListViewAutomationPeer : FrameworkElementAutomationPeer, ISelectionProvider
{
    public ListViewAutomationPeer(ListView owner) : base(owner) { }

    private ListView ListViewOwner => (ListView)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.List;

    protected override string GetClassNameCore() => nameof(ListView);

    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Selection)
            return this;
        return base.GetPatternCore(patternInterface);
    }

    public AutomationPeer[] GetSelection() => Array.Empty<AutomationPeer>();
    public bool IsSelectionRequired => false;
    public bool CanSelectMultiple => ListViewOwner.SelectionMode != SelectionMode.Single;
}

#endregion

#region DatePicker / Calendar / TimePicker

/// <summary>
/// Exposes DatePicker types to UI Automation.
/// </summary>
public sealed class DatePickerAutomationPeer : FrameworkElementAutomationPeer, IValueProvider
{
    public DatePickerAutomationPeer(DatePicker owner) : base(owner) { }

    private DatePicker DatePickerOwner => (DatePicker)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(DatePicker);

    protected override string GetNameCore()
    {
        var header = DatePickerOwner.Header;
        if (header is string text) return text;
        return header?.ToString() ?? base.GetNameCore();
    }

    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Value)
            return this;
        return base.GetPatternCore(patternInterface);
    }

    public string Value => DatePickerOwner.SelectedDate?.ToString("d") ?? string.Empty;
    public bool IsReadOnly => !IsEnabled();

    public void SetValue(string value)
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot set value on a disabled DatePicker.");
        if (DateTime.TryParse(value, out var date))
            DatePickerOwner.SelectedDate = date;
    }
}

/// <summary>
/// Exposes Calendar types to UI Automation.
/// </summary>
public sealed class CalendarAutomationPeer : FrameworkElementAutomationPeer
{
    public CalendarAutomationPeer(Calendar owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Calendar;

    protected override string GetClassNameCore() => nameof(Calendar);
}

/// <summary>
/// Exposes TimePicker types to UI Automation.
/// </summary>
public sealed class TimePickerAutomationPeer : FrameworkElementAutomationPeer, IValueProvider
{
    public TimePickerAutomationPeer(TimePicker owner) : base(owner) { }

    private TimePicker TimePickerOwner => (TimePicker)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(TimePicker);

    protected override string GetNameCore()
    {
        var header = TimePickerOwner.Header;
        if (header is string text) return text;
        return header?.ToString() ?? base.GetNameCore();
    }

    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Value)
            return this;
        return base.GetPatternCore(patternInterface);
    }

    public string Value => TimePickerOwner.SelectedTime?.ToString(@"hh\:mm") ?? string.Empty;
    public bool IsReadOnly => !IsEnabled();

    public void SetValue(string value)
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot set value on a disabled TimePicker.");
        if (TimeSpan.TryParse(value, out var time))
            TimePickerOwner.SelectedTime = time;
    }
}

#endregion

#region NavigationView

/// <summary>
/// Exposes NavigationView types to UI Automation.
/// </summary>
public sealed class NavigationViewAutomationPeer : FrameworkElementAutomationPeer, IExpandCollapseProvider, ISelectionProvider
{
    public NavigationViewAutomationPeer(NavigationView owner) : base(owner) { }

    private NavigationView NavViewOwner => (NavigationView)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Pane;

    protected override string GetClassNameCore() => nameof(NavigationView);

    protected override string GetNameCore()
    {
        var header = NavViewOwner.Header;
        if (header is string text) return text;
        return header?.ToString() ?? base.GetNameCore();
    }

    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.ExpandCollapse)
            return this;
        if (patternInterface == PatternInterface.Selection)
            return this;
        return base.GetPatternCore(patternInterface);
    }

    public ExpandCollapseState ExpandCollapseState =>
        NavViewOwner.IsPaneOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand()
    {
        if (!IsEnabled()) throw new InvalidOperationException("Cannot expand a disabled NavigationView.");
        NavViewOwner.IsPaneOpen = true;
    }

    public void Collapse()
    {
        if (!IsEnabled()) throw new InvalidOperationException("Cannot collapse a disabled NavigationView.");
        NavViewOwner.IsPaneOpen = false;
    }

    public AutomationPeer[] GetSelection() => Array.Empty<AutomationPeer>();
    public bool IsSelectionRequired => false;
    public bool CanSelectMultiple => false;
}

/// <summary>
/// Exposes NavigationViewItem types to UI Automation.
/// </summary>
public sealed class NavigationViewItemAutomationPeer : FrameworkElementAutomationPeer, ISelectionItemProvider, IInvokeProvider
{
    public NavigationViewItemAutomationPeer(NavigationViewItem owner) : base(owner) { }

    private NavigationViewItem ItemOwner => (NavigationViewItem)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.ListItem;

    protected override string GetClassNameCore() => nameof(NavigationViewItem);

    protected override string GetNameCore()
    {
        var content = ItemOwner.Content;
        if (content is string text) return text;
        return content?.ToString() ?? base.GetNameCore();
    }

    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.SelectionItem)
            return this;
        if (patternInterface == PatternInterface.Invoke)
            return this;
        return base.GetPatternCore(patternInterface);
    }

    public bool IsSelected => ItemOwner.IsSelected;

    public AutomationPeer SelectionContainer
    {
        get
        {
            var parent = ItemOwner.VisualParent;
            while (parent != null)
            {
                if (parent is NavigationView nav)
                    return nav.GetAutomationPeer() ?? new NavigationViewAutomationPeer(nav);
                parent = (parent as FrameworkElement)?.VisualParent;
            }
            return null!;
        }
    }

    public void Select() => ItemOwner.IsSelected = true;
    public void AddToSelection() => ItemOwner.IsSelected = true;
    public void RemoveFromSelection() => ItemOwner.IsSelected = false;

    public void Invoke()
    {
        if (!IsEnabled()) throw new InvalidOperationException("Cannot invoke a disabled NavigationViewItem.");
        ItemOwner.IsSelected = true;
        RaiseAutomationEvent(AutomationEvents.InvokePatternOnInvoked);
    }
}

#endregion

#region NumberBox

/// <summary>
/// Exposes NumberBox types to UI Automation.
/// </summary>
public sealed class NumberBoxAutomationPeer : FrameworkElementAutomationPeer, IRangeValueProvider
{
    public NumberBoxAutomationPeer(NumberBox owner) : base(owner) { }

    private NumberBox NumberBoxOwner => (NumberBox)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Spinner;

    protected override string GetClassNameCore() => nameof(NumberBox);

    protected override string GetNameCore()
    {
        var header = NumberBoxOwner.Header;
        if (header is string text) return text;
        return header?.ToString() ?? base.GetNameCore();
    }

    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.RangeValue)
            return this;
        return base.GetPatternCore(patternInterface);
    }

    public double Value => NumberBoxOwner.Value;
    public double Minimum => NumberBoxOwner.Minimum;
    public double Maximum => NumberBoxOwner.Maximum;
    public double SmallChange => NumberBoxOwner.SmallChange;
    public double LargeChange => NumberBoxOwner.LargeChange;
    public bool IsReadOnly => !IsEnabled();

    public void SetValue(double value)
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot set value on a disabled NumberBox.");
        if (value < Minimum || value > Maximum)
            throw new ArgumentOutOfRangeException(nameof(value));
        NumberBoxOwner.Value = value;
    }
}

#endregion

#region ToggleSwitch

/// <summary>
/// Exposes ToggleSwitch types to UI Automation.
/// </summary>
public sealed class ToggleSwitchAutomationPeer : FrameworkElementAutomationPeer, IToggleProvider
{
    public ToggleSwitchAutomationPeer(ToggleSwitch owner) : base(owner) { }

    private ToggleSwitch SwitchOwner => (ToggleSwitch)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(ToggleSwitch);

    protected override string GetNameCore()
    {
        var header = SwitchOwner.Header;
        if (header is string text) return text;
        return header?.ToString() ?? base.GetNameCore();
    }

    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Toggle)
            return this;
        return base.GetPatternCore(patternInterface);
    }

    public ToggleState ToggleState =>
        SwitchOwner.IsOn ? ToggleState.On : ToggleState.Off;

    public void Toggle()
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot toggle a disabled ToggleSwitch.");
        SwitchOwner.IsOn = !SwitchOwner.IsOn;
    }
}

#endregion

#region ColorPicker / InfoBar

/// <summary>
/// Exposes ColorPicker types to UI Automation.
/// </summary>
public sealed class ColorPickerAutomationPeer : FrameworkElementAutomationPeer, IValueProvider
{
    public ColorPickerAutomationPeer(ColorPicker owner) : base(owner) { }

    private ColorPicker PickerOwner => (ColorPicker)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(ColorPicker);

    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Value)
            return this;
        return base.GetPatternCore(patternInterface);
    }

    public string Value => PickerOwner.Color.ToString();
    public bool IsReadOnly => !IsEnabled();

    public void SetValue(string value)
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot set value on a disabled ColorPicker.");
    }
}

/// <summary>
/// Exposes InfoBar types to UI Automation.
/// </summary>
public sealed class InfoBarAutomationPeer : FrameworkElementAutomationPeer
{
    public InfoBarAutomationPeer(InfoBar owner) : base(owner) { }

    private InfoBar InfoBarOwner => (InfoBar)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(InfoBar);

    protected override string GetNameCore()
        => InfoBarOwner.Title ?? InfoBarOwner.Message ?? base.GetNameCore();
}

/// <summary>
/// Exposes ToastNotificationItem types to UI Automation.
/// </summary>
public sealed class ToastNotificationItemAutomationPeer : FrameworkElementAutomationPeer
{
    public ToastNotificationItemAutomationPeer(ToastNotificationItem owner) : base(owner) { }

    private ToastNotificationItem ToastOwner => (ToastNotificationItem)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(ToastNotificationItem);

    protected override string GetNameCore()
        => ToastOwner.Title ?? ToastOwner.Message ?? base.GetNameCore();
}

/// <summary>
/// Exposes ToastNotificationHost types to UI Automation.
/// </summary>
public sealed class ToastNotificationHostAutomationPeer : FrameworkElementAutomationPeer
{
    public ToastNotificationHostAutomationPeer(ToastNotificationHost owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(ToastNotificationHost);
}

#endregion

#region AutoCompleteBox

/// <summary>
/// Exposes AutoCompleteBox types to UI Automation.
/// </summary>
public sealed class AutoCompleteBoxAutomationPeer : FrameworkElementAutomationPeer, IValueProvider
{
    public AutoCompleteBoxAutomationPeer(AutoCompleteBox owner) : base(owner) { }

    private AutoCompleteBox AutoCompleteOwner => (AutoCompleteBox)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Edit;

    protected override string GetClassNameCore() => nameof(AutoCompleteBox);

    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Value)
            return this;
        return base.GetPatternCore(patternInterface);
    }

    public string Value => AutoCompleteOwner.Text ?? string.Empty;
    public bool IsReadOnly => AutoCompleteOwner.IsReadOnly;

    public void SetValue(string value)
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot set value on a disabled AutoCompleteBox.");
        if (IsReadOnly)
            throw new InvalidOperationException("Cannot set value on a read-only AutoCompleteBox.");
        AutoCompleteOwner.Text = value ?? string.Empty;
    }
}

#endregion

#region WebView / Popup / Viewbox

/// <summary>
/// Exposes WebView types to UI Automation.
/// </summary>
public sealed class WebViewAutomationPeer : FrameworkElementAutomationPeer
{
    public WebViewAutomationPeer(WebView owner) : base(owner) { }

    private WebView WebViewOwner => (WebView)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Pane;

    protected override string GetClassNameCore() => nameof(WebView);

    protected override string GetNameCore()
        => WebViewOwner.DocumentTitle ?? base.GetNameCore();
}

/// <summary>
/// Exposes Popup types to UI Automation.
/// </summary>
public sealed class PopupAutomationPeer : FrameworkElementAutomationPeer
{
    public PopupAutomationPeer(Popup owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Window;

    protected override string GetClassNameCore() => nameof(Popup);

    protected override bool IsControlElementCore() => false;
}

/// <summary>
/// Exposes Viewbox types to UI Automation.
/// </summary>
public sealed class ViewboxAutomationPeer : FrameworkElementAutomationPeer
{
    public ViewboxAutomationPeer(Viewbox owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(Viewbox);

    protected override bool IsControlElementCore() => false;
}

#endregion

#region ItemsControl / ContentControl

/// <summary>
/// Exposes ItemsControl types to UI Automation.
/// </summary>
public sealed class ItemsControlAutomationPeer : FrameworkElementAutomationPeer
{
    public ItemsControlAutomationPeer(ItemsControl owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.List;

    protected override string GetClassNameCore() => nameof(ItemsControl);
}

/// <summary>
/// Exposes ContentControl types to UI Automation.
/// </summary>
public sealed class ContentControlAutomationPeer : FrameworkElementAutomationPeer
{
    public ContentControlAutomationPeer(ContentControl owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(ContentControl);

    protected override string GetNameCore()
    {
        var content = ((ContentControl)Owner).Content;
        if (content is string text) return text;
        return content?.ToString() ?? base.GetNameCore();
    }
}

#endregion

#region TitleBarButton

/// <summary>
/// Exposes TitleBarButton types to UI Automation.
/// </summary>
public sealed class TitleBarButtonAutomationPeer : ButtonBaseAutomationPeer
{
    public TitleBarButtonAutomationPeer(TitleBarButton owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Button;

    protected override string GetClassNameCore() => nameof(TitleBarButton);
}

#endregion

#region InkCanvas

/// <summary>
/// Exposes InkCanvas types to UI Automation.
/// </summary>
public sealed class InkCanvasAutomationPeer : FrameworkElementAutomationPeer
{
    public InkCanvasAutomationPeer(InkCanvas owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(InkCanvas);

    protected override string GetLocalizedControlTypeCore() => "ink canvas";
}

#endregion

#region Label / Separator / UserControl

/// <summary>
/// Exposes Label types to UI Automation.
/// </summary>
public sealed class LabelAutomationPeer : FrameworkElementAutomationPeer
{
    public LabelAutomationPeer(Label owner) : base(owner) { }

    private Label LabelOwner => (Label)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Text;

    protected override string GetClassNameCore() => nameof(Label);

    protected override string GetNameCore()
    {
        var content = LabelOwner.Content;
        if (content is string text) return text;
        return content?.ToString() ?? base.GetNameCore();
    }
}

/// <summary>
/// Exposes Separator types to UI Automation.
/// </summary>
public sealed class SeparatorAutomationPeer : FrameworkElementAutomationPeer
{
    public SeparatorAutomationPeer(Separator owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Separator;

    protected override string GetClassNameCore() => nameof(Separator);

    protected override bool IsControlElementCore() => false;
}

/// <summary>
/// Exposes UserControl types to UI Automation.
/// </summary>
public sealed class UserControlAutomationPeer : FrameworkElementAutomationPeer
{
    public UserControlAutomationPeer(UserControl owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(UserControl);
}

#endregion

#region StatusBar / ToolBar

/// <summary>
/// Exposes StatusBar types to UI Automation.
/// </summary>
public sealed class StatusBarAutomationPeer : FrameworkElementAutomationPeer
{
    public StatusBarAutomationPeer(StatusBar owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.StatusBar;

    protected override string GetClassNameCore() => nameof(StatusBar);
}

/// <summary>
/// Exposes StatusBarItem types to UI Automation.
/// </summary>
public sealed class StatusBarItemAutomationPeer : FrameworkElementAutomationPeer
{
    public StatusBarItemAutomationPeer(StatusBarItem owner) : base(owner) { }

    private StatusBarItem ItemOwner => (StatusBarItem)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(StatusBarItem);

    protected override string GetNameCore()
    {
        var content = ItemOwner.Content;
        if (content is string text) return text;
        return content?.ToString() ?? base.GetNameCore();
    }
}

/// <summary>
/// Exposes ToolBar types to UI Automation.
/// </summary>
public sealed class ToolBarAutomationPeer : FrameworkElementAutomationPeer
{
    public ToolBarAutomationPeer(ToolBar owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.ToolBar;

    protected override string GetClassNameCore() => nameof(ToolBar);
}

#endregion

#region Frame / NavigationWindow

/// <summary>
/// Exposes Frame types to UI Automation.
/// </summary>
public sealed class FrameAutomationPeer : FrameworkElementAutomationPeer
{
    public FrameAutomationPeer(Frame owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Pane;

    protected override string GetClassNameCore() => nameof(Frame);
}

/// <summary>
/// Exposes NavigationWindow types to UI Automation.
/// </summary>
public sealed class NavigationWindowAutomationPeer : FrameworkElementAutomationPeer
{
    public NavigationWindowAutomationPeer(NavigationWindow owner) : base(owner) { }

    private NavigationWindow NavWindowOwner => (NavigationWindow)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Window;

    protected override string GetClassNameCore() => nameof(NavigationWindow);

    protected override string GetNameCore()
        => NavWindowOwner.Title ?? base.GetNameCore();
}

#endregion

#region Viewport3D / DocumentViewer

/// <summary>
/// Exposes Viewport3D types to UI Automation.
/// </summary>
public sealed class Viewport3DAutomationPeer : FrameworkElementAutomationPeer
{
    public Viewport3DAutomationPeer(Viewport3D owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(Viewport3D);

    protected override string GetLocalizedControlTypeCore() => "viewport 3D";
}

/// <summary>
/// Exposes DocumentViewer types to UI Automation.
/// </summary>
public sealed class DocumentViewerAutomationPeer : FrameworkElementAutomationPeer
{
    public DocumentViewerAutomationPeer(DocumentViewer owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Document;

    protected override string GetClassNameCore() => nameof(DocumentViewer);
}

#endregion

#region DataGrid Sub-elements

/// <summary>
/// Exposes DataGridRow types to UI Automation.
/// </summary>
public sealed class DataGridRowAutomationPeer : FrameworkElementAutomationPeer
{
    public DataGridRowAutomationPeer(DataGridRow owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.DataItem;

    protected override string GetClassNameCore() => nameof(DataGridRow);
}

/// <summary>
/// Exposes DataGridCell types to UI Automation.
/// </summary>
public sealed class DataGridCellAutomationPeer : FrameworkElementAutomationPeer
{
    public DataGridCellAutomationPeer(DataGridCell owner) : base(owner) { }

    private DataGridCell CellOwner => (DataGridCell)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(DataGridCell);

    protected override string GetNameCore()
    {
        var content = CellOwner.Content;
        if (content is string text) return text;
        return content?.ToString() ?? base.GetNameCore();
    }
}

/// <summary>
/// Exposes DataGridColumnHeader types to UI Automation.
/// </summary>
public sealed class DataGridColumnHeaderAutomationPeer : FrameworkElementAutomationPeer
{
    public DataGridColumnHeaderAutomationPeer(DataGridColumnHeader owner) : base(owner) { }

    private DataGridColumnHeader HeaderOwner => (DataGridColumnHeader)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.HeaderItem;

    protected override string GetClassNameCore() => nameof(DataGridColumnHeader);

    protected override string GetNameCore()
    {
        var content = HeaderOwner.Content;
        if (content is string text) return text;
        return content?.ToString() ?? base.GetNameCore();
    }
}

#endregion

#region GroupItem

/// <summary>
/// Exposes GroupItem types to UI Automation.
/// </summary>
public sealed class GroupItemAutomationPeer : FrameworkElementAutomationPeer
{
    public GroupItemAutomationPeer(GroupItem owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(GroupItem);
}

#endregion

#region Generic Automation Peer

/// <summary>
/// Generic automation peer for controls that need a specific control type
/// but no special pattern support. Avoids creating one-off peer classes.
/// </summary>
internal sealed class GenericAutomationPeer : FrameworkElementAutomationPeer
{
    private readonly AutomationControlType _controlType;

    public GenericAutomationPeer(FrameworkElement owner, AutomationControlType controlType)
        : base(owner) => _controlType = controlType;

    protected override AutomationControlType GetAutomationControlTypeCore() => _controlType;

    protected override string GetClassNameCore() => Owner.GetType().Name;

    protected override string GetNameCore()
    {
        if (Owner is ContentControl cc && cc.Content is string text)
            return text;
        return base.GetNameCore();
    }
}

#endregion

#region ScrollBar

/// <summary>
/// Exposes ScrollBar types to UI Automation.
/// </summary>
public sealed class ScrollBarAutomationPeer : FrameworkElementAutomationPeer, IRangeValueProvider
{
    public ScrollBarAutomationPeer(ScrollBar owner) : base(owner) { }

    private ScrollBar ScrollBarOwner => (ScrollBar)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.ScrollBar;

    protected override string GetClassNameCore() => nameof(ScrollBar);

    protected override object? GetPatternCore(PatternInterface patternInterface)
        => patternInterface == PatternInterface.RangeValue ? this : base.GetPatternCore(patternInterface);

    public void SetValue(double value) => ScrollBarOwner.Value = value;
    public double Value => ScrollBarOwner.Value;
    public bool IsReadOnly => false;
    public double Maximum => ScrollBarOwner.Maximum;
    public double Minimum => ScrollBarOwner.Minimum;
    public double LargeChange => ScrollBarOwner.LargeChange;
    public double SmallChange => ScrollBarOwner.SmallChange;
}

#endregion

#region MenuBar / MenuBarItem / MenuFlyoutItem

/// <summary>
/// Exposes MenuBar types to UI Automation.
/// </summary>
public sealed class MenuBarAutomationPeer : FrameworkElementAutomationPeer
{
    public MenuBarAutomationPeer(MenuBar owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.MenuBar;

    protected override string GetClassNameCore() => nameof(MenuBar);
}

/// <summary>
/// Exposes MenuBarItem types to UI Automation.
/// </summary>
public sealed class MenuBarItemAutomationPeer : FrameworkElementAutomationPeer, IExpandCollapseProvider, IInvokeProvider
{
    public MenuBarItemAutomationPeer(MenuBarItem owner) : base(owner) { }

    private MenuBarItem MenuBarItemOwner => (MenuBarItem)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.MenuItem;

    protected override string GetClassNameCore() => nameof(MenuBarItem);

    protected override string GetNameCore()
        => MenuBarItemOwner.Title ?? base.GetNameCore();

    protected override object? GetPatternCore(PatternInterface patternInterface) => patternInterface switch
    {
        PatternInterface.ExpandCollapse => this,
        PatternInterface.Invoke => this,
        _ => base.GetPatternCore(patternInterface),
    };

    public void Expand() { /* MenuBarItem opens via click */ }
    public void Collapse() { /* MenuBarItem closes via click */ }
    public ExpandCollapseState ExpandCollapseState
        => MenuBarItemOwner.IsMenuOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
    public void Invoke() { /* trigger click */ }
}

/// <summary>
/// Exposes MenuFlyoutItem types to UI Automation.
/// </summary>
public sealed class MenuFlyoutItemAutomationPeer : FrameworkElementAutomationPeer, IInvokeProvider
{
    public MenuFlyoutItemAutomationPeer(MenuFlyoutItem owner) : base(owner) { }

    private MenuFlyoutItem MenuFlyoutItemOwner => (MenuFlyoutItem)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.MenuItem;

    protected override string GetClassNameCore() => nameof(MenuFlyoutItem);

    protected override string GetNameCore()
    {
        var text = MenuFlyoutItemOwner.Text;
        if (!string.IsNullOrEmpty(text)) return text;
        return base.GetNameCore();
    }

    protected override object? GetPatternCore(PatternInterface patternInterface)
        => patternInterface == PatternInterface.Invoke ? this : base.GetPatternCore(patternInterface);

    public void Invoke() => MenuFlyoutItemOwner.Command?.Execute(MenuFlyoutItemOwner.CommandParameter);
}

#endregion

#region TitleBar

/// <summary>
/// Exposes TitleBar types to UI Automation.
/// </summary>
public sealed class TitleBarAutomationPeer : FrameworkElementAutomationPeer
{
    public TitleBarAutomationPeer(TitleBar owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.TitleBar;

    protected override string GetClassNameCore() => nameof(TitleBar);

    protected override string GetNameCore()
    {
        var title = ((TitleBar)Owner).Title;
        return !string.IsNullOrEmpty(title) ? title : base.GetNameCore();
    }
}

#endregion

#region CommandBar

/// <summary>
/// Exposes CommandBar types to UI Automation.
/// </summary>
public sealed class CommandBarAutomationPeer : FrameworkElementAutomationPeer
{
    public CommandBarAutomationPeer(CommandBar owner) : base(owner) { }

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.ToolBar;

    protected override string GetClassNameCore() => nameof(CommandBar);
}

#endregion
