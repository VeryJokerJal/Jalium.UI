using Jalium.UI.Automation;
using Jalium.UI.Documents;
using Jalium.UI.Controls.Primitives;
using IInvokeProvider = Jalium.UI.Automation.Provider.IInvokeProvider;
using ISelectionItemProvider = Jalium.UI.Automation.Provider.ISelectionItemProvider;

namespace Jalium.UI.Automation.Peers;

public class CalendarButtonAutomationPeer : FrameworkElementAutomationPeer
{
    public CalendarButtonAutomationPeer(CalendarButton owner) : base(owner) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
    protected override string GetClassNameCore() => nameof(CalendarButton);
}

public class ContextMenuAutomationPeer : FrameworkElementAutomationPeer
{
    public ContextMenuAutomationPeer(ContextMenu owner) : base(owner) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Menu;
    protected override string GetClassNameCore() => nameof(ContextMenu);
}

public class FrameworkContentElementAutomationPeer : ContentElementAutomationPeer
{
    public FrameworkContentElementAutomationPeer(FrameworkContentElement owner) : base(owner) { }
    public new FrameworkContentElement Owner => (FrameworkContentElement)base.Owner;
    protected override string GetClassNameCore() => Owner.GetType().Name;
}

public class ContentTextAutomationPeer : FrameworkContentElementAutomationPeer
{
    public ContentTextAutomationPeer(FrameworkContentElement owner) : base(owner) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
}

public class TextElementAutomationPeer : ContentTextAutomationPeer
{
    public TextElementAutomationPeer(TextElement owner) : base(owner) { }
    public new TextElement Owner => (TextElement)base.Owner;
}

public class HyperlinkAutomationPeer : TextElementAutomationPeer, IInvokeProvider
{
    public HyperlinkAutomationPeer(Hyperlink owner) : base(owner) { }
    public void Invoke() => Owner.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent, Owner));
    public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Hyperlink;
}

public class DocumentAutomationPeer : ContentTextAutomationPeer
{
    public DocumentAutomationPeer(FrameworkContentElement owner) : base(owner) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;
}

public class TableAutomationPeer : TextElementAutomationPeer
{
    public TableAutomationPeer(Table owner) : base(owner) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Table;
}

public class TableCellAutomationPeer : TextElementAutomationPeer
{
    public TableCellAutomationPeer(TableCell owner) : base(owner) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataItem;
}

public class DataGridColumnHeadersPresenterAutomationPeer : ItemsControlAutomationPeer
{
    public DataGridColumnHeadersPresenterAutomationPeer(DataGridColumnHeadersPresenter owner) : base(owner) { }
    protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new DataGridColumnHeaderItemAutomationPeer(item, this);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Header;
    protected override string GetClassNameCore() => nameof(DataGridColumnHeadersPresenter);
}

public class DataGridColumnHeaderItemAutomationPeer : ItemAutomationPeer
{
    public DataGridColumnHeaderItemAutomationPeer(object item, ItemsControlAutomationPeer parent) : base(item, parent) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.HeaderItem;
}

public class DataGridItemAutomationPeer : ItemAutomationPeer,
    Jalium.UI.Automation.Provider.ISelectionItemProvider
{
    private readonly DataGridAutomationPeer _parent;

    public DataGridItemAutomationPeer(object item, DataGridAutomationPeer parent) : base(item, parent)
    {
        _parent = parent;
    }

    private DataGrid DataGridOwner => (DataGrid)_parent.Owner;

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.SelectionItem ? this : base.GetPattern(patternInterface);

    public bool IsSelected => DataGridOwner.SelectedItems.Contains(Item);
    public AutomationPeer SelectionContainer => _parent;
    public void Select() => _ = DataGridOwner.AutomationSelectItem(Item, addToSelection: false);
    public void AddToSelection() => _ = DataGridOwner.AutomationSelectItem(Item, addToSelection: true);
    public void RemoveFromSelection() => _ = DataGridOwner.AutomationRemoveItemFromSelection(Item);

    bool Jalium.UI.Automation.Provider.ISelectionItemProvider.IsSelected => IsSelected;
    Jalium.UI.Automation.Provider.IRawElementProviderSimple Jalium.UI.Automation.Provider.ISelectionItemProvider.SelectionContainer =>
        ProviderFromPeer(_parent);
    void Jalium.UI.Automation.Provider.ISelectionItemProvider.Select() => Select();
    void Jalium.UI.Automation.Provider.ISelectionItemProvider.AddToSelection() => AddToSelection();
    void Jalium.UI.Automation.Provider.ISelectionItemProvider.RemoveFromSelection() => RemoveFromSelection();

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataItem;
}

public class TreeViewDataItemAutomationPeer : ItemAutomationPeer
{
    public TreeViewDataItemAutomationPeer(object item, ItemsControlAutomationPeer parent) : base(item, parent) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TreeItem;
}

public class GridViewItemAutomationPeer : ListBoxItemAutomationPeer
{
    public GridViewItemAutomationPeer(object item, ListBoxAutomationPeer parent) : base(item, parent) { }
}

public class GridViewCellAutomationPeer : FrameworkElementAutomationPeer
{
    public GridViewCellAutomationPeer(FrameworkElement owner) : base(owner) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataItem;
    protected override string GetClassNameCore() => nameof(GridViewCellAutomationPeer).Replace("AutomationPeer", string.Empty);
}

public class GridViewColumnHeaderAutomationPeer : FrameworkElementAutomationPeer
{
    public GridViewColumnHeaderAutomationPeer(GridViewColumnHeader owner) : base(owner) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.HeaderItem;
    protected override string GetClassNameCore() => nameof(GridViewColumnHeader);
}

public class GridViewHeaderRowPresenterAutomationPeer : FrameworkElementAutomationPeer
{
    public GridViewHeaderRowPresenterAutomationPeer(GridViewHeaderRowPresenter owner) : base(owner) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Header;
    protected override string GetClassNameCore() => nameof(GridViewHeaderRowPresenter);
}

public class DataGridDetailsPresenterAutomationPeer : FrameworkElementAutomationPeer
{
    public DataGridDetailsPresenterAutomationPeer(DataGridDetailsPresenter owner) : base(owner) { }
    protected override string GetClassNameCore() => nameof(DataGridDetailsPresenter);
}

public class DataGridRowHeaderAutomationPeer : ButtonBaseAutomationPeer
{
    public DataGridRowHeaderAutomationPeer(DataGridRowHeader owner) : base(owner) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.HeaderItem;
    protected override string GetClassNameCore() => nameof(DataGridRowHeader);
}

public class DocumentPageViewAutomationPeer : FrameworkElementAutomationPeer
{
    public DocumentPageViewAutomationPeer(DocumentPageView owner) : base(owner) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;
    protected override string GetClassNameCore() => nameof(DocumentPageView);
}

public class DocumentViewerBaseAutomationPeer : FrameworkElementAutomationPeer
{
    public DocumentViewerBaseAutomationPeer(DocumentViewerBase owner) : base(owner) { }
    public override object? GetPattern(PatternInterface patternInterface) => base.GetPattern(patternInterface);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;
    protected override string GetClassNameCore() => "DocumentViewer";
}

public class FixedPageAutomationPeer : FrameworkElementAutomationPeer
{
    public FixedPageAutomationPeer(FixedPage owner) : base(owner) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
    protected override string GetClassNameCore() => nameof(FixedPage);
}

public class FlowDocumentPageViewerAutomationPeer : DocumentViewerBaseAutomationPeer
{
    public FlowDocumentPageViewerAutomationPeer(FlowDocumentPageViewer owner) : base(owner) { }
    protected override string GetClassNameCore() => nameof(FlowDocumentPageViewer);
}

public class FlowDocumentReaderAutomationPeer : FrameworkElementAutomationPeer
{
    public FlowDocumentReaderAutomationPeer(FlowDocumentReader owner) : base(owner) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;
    protected override string GetClassNameCore() => nameof(FlowDocumentReader);
}

public class FlowDocumentScrollViewerAutomationPeer : FrameworkElementAutomationPeer
{
    public FlowDocumentScrollViewerAutomationPeer(FlowDocumentScrollViewer owner) : base(owner) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;
    protected override string GetClassNameCore() => nameof(FlowDocumentScrollViewer);
}

public class InkPresenterAutomationPeer : FrameworkElementAutomationPeer
{
    public InkPresenterAutomationPeer(InkPresenter owner) : base(owner) { }
    protected override string GetClassNameCore() => nameof(InkPresenter);
}

public class ListBoxItemWrapperAutomationPeer : FrameworkElementAutomationPeer
{
    public ListBoxItemWrapperAutomationPeer(FrameworkElement owner) : base(owner) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
    protected override string GetClassNameCore() => nameof(ListBoxItemWrapperAutomationPeer).Replace("AutomationPeer", string.Empty);
}

public class TabItemWrapperAutomationPeer : FrameworkElementAutomationPeer
{
    public TabItemWrapperAutomationPeer(FrameworkElement owner) : base(owner) { }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TabItem;
    protected override string GetClassNameCore() => nameof(TabItemWrapperAutomationPeer).Replace("AutomationPeer", string.Empty);
}

public class DateTimeAutomationPeer : AutomationPeer, IInvokeProvider, ISelectionItemProvider
{
    private readonly CalendarAutomationPeer _parent;
    public DateTimeAutomationPeer(DateTime value, CalendarAutomationPeer parent) { Value = value; _parent = parent; }
    public DateTime Value { get; }
    public override object? GetPattern(PatternInterface patternInterface) => patternInterface is PatternInterface.Invoke or PatternInterface.SelectionItem ? this : null;
    public void Invoke() => ((Calendar)_parent.Owner).SelectedDate = Value;
    public bool IsSelected => ((Calendar)_parent.Owner).SelectedDates.Contains(Value);
    public AutomationPeer SelectionContainer => _parent;
    Jalium.UI.Automation.Provider.IRawElementProviderSimple ISelectionItemProvider.SelectionContainer =>
        ProviderFromPeer(_parent);
    public void Select() => ((Calendar)_parent.Owner).SelectedDate = Value;
    public void AddToSelection() => ((Calendar)_parent.Owner).SelectedDates.Add(Value);
    public void RemoveFromSelection() => ((Calendar)_parent.Owner).SelectedDates.Remove(Value);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
    protected override string GetClassNameCore() => nameof(DateTime);
    protected override string GetNameCore() => Value.ToLongDateString();
    protected override string GetAutomationIdCore() => Value.ToString("yyyy-MM-dd");
    protected override string GetHelpTextCore() => string.Empty;
    protected override string GetAcceleratorKeyCore() => string.Empty;
    protected override string GetAccessKeyCore() => string.Empty;
    protected override string GetItemStatusCore() => string.Empty;
    protected override string GetItemTypeCore() => "Date";
    protected override string GetLocalizedControlTypeCore() => "date";
    protected override Rect GetBoundingRectangleCore() => Rect.Empty;
    protected override Point GetClickablePointCore() => new(double.NaN, double.NaN);
    protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;
    protected override bool IsEnabledCore() => true;
    protected override bool IsKeyboardFocusableCore() => true;
    protected override bool HasKeyboardFocusCore() => false;
    protected override bool IsOffscreenCore() => false;
    protected override bool IsContentElementCore() => true;
    protected override bool IsControlElementCore() => true;
    protected override bool IsPasswordCore() => false;
    protected override bool IsRequiredForFormCore() => false;
    protected override AutomationPeer? GetLabeledByCore() => null;
    protected override AutomationPeer? GetParentCore() => _parent;
    protected override List<AutomationPeer> GetChildrenCore() => [];
    protected override void SetFocusCore() { }
}

public class UIElement3DAutomationPeer : AutomationPeer
{
    public UIElement3DAutomationPeer(UIElement3D owner) { Owner = owner ?? throw new ArgumentNullException(nameof(owner)); }
    public new UIElement3D Owner { get; }
    public override object? GetPattern(PatternInterface patternInterface) => null;
    protected override DependencyObject GetAutomationOwnerCore() => Owner;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
    protected override string GetClassNameCore() => Owner.GetType().Name;
    protected override string GetNameCore() => string.Empty;
    protected override string GetAutomationIdCore() => string.Empty;
    protected override string GetHelpTextCore() => string.Empty;
    protected override string GetAcceleratorKeyCore() => string.Empty;
    protected override string GetAccessKeyCore() => string.Empty;
    protected override string GetItemStatusCore() => string.Empty;
    protected override string GetItemTypeCore() => string.Empty;
    protected override string GetLocalizedControlTypeCore() => "custom";
    protected override Rect GetBoundingRectangleCore() => Rect.Empty;
    protected override Point GetClickablePointCore() => new(double.NaN, double.NaN);
    protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;
    protected override bool IsEnabledCore() => true;
    protected override bool IsKeyboardFocusableCore() => false;
    protected override bool HasKeyboardFocusCore() => false;
    protected override bool IsOffscreenCore() => false;
    protected override bool IsContentElementCore() => true;
    protected override bool IsControlElementCore() => true;
    protected override bool IsPasswordCore() => false;
    protected override bool IsRequiredForFormCore() => false;
    protected override AutomationPeer? GetLabeledByCore() => null;
    protected override AutomationPeer? GetParentCore() => null;
    protected override List<AutomationPeer> GetChildrenCore() => [];
    protected override void SetFocusCore() { }
}
