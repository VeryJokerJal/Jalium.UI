using AppKit;
using Foundation;
using ObjCRuntime;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using SelectionControl = Jalium.UI.Controls.Primitives.Selector;
using Jalium.UI.Data;
using Jalium.UI.Interop;
using Jalium.UI.Styling;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>Checks the capabilities and visible content advertised to AppKit clients.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class AccessibilitySemanticsChecks
{
    private static readonly string[] s_names =
    [
        "ordinary controls do not advertise selection, disclosure or range attributes",
        "selection capabilities survive unselected and disabled states",
        "outline branches disclose while leaf rows remain selectable",
        "data templates expose visible labels and honor explicit names",
        "CSS hidden boxes omit invisible peers and retain explicitly visible descendants",
        "CSS display none gates the complete subtree and preserves reappearance identity",
        "list selection actions synchronize collections and selection events",
        "AX selection transfers two-way binding values and preserves one-way sources"
    ];

    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < s_names.Length; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--accessibility-semantics-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000)) { process.Kill(); failed++; Console.Error.WriteLine($"FAIL: AX semantics case {index} timed out"); }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS accessibility semantics host checks: {s_names.Length - failed}/{s_names.Length} passed");
        return failed == 0 ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--accessibility-semantics-case=".Length), out int index) || (uint)index >= s_names.Length) return 2;
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var panel = new StackPanel { Margin = new Thickness(24) };
        var button = new Button { Content = "保存中文", Height = 32 };
        var label = new TextBlock { Text = "中文说明", Height = 28 };
        Id(button, "command"); Id(label, "label");
        panel.Children.Add(button); panel.Children.Add(label);
        var window = new Window { Title = "窗口内容语义验证", Width = 560, Height = 640,
            TitleBarStyle = WindowTitleBarStyle.Native, ShowActivated = false, Content = panel };
        try
        {
            window.Show(); window.UpdateLayout();
            var view = Runtime.GetNSObject<NSView>(window.Handle)!;
            var root = (view.AccessibilityChildren ?? []).OfType<NSAccessibilityElement>().Single();
            switch (index)
            {
                case 0:
                    foreach (var element in new[] { root, Find(root, "command"), Find(root, "label") })
                    {
                        foreach (string selector in new[] { "isAccessibilitySelected", "isAccessibilityExpanded", "isAccessibilityDisclosed",
                            "accessibilitySelectedChildren", "accessibilityRows", "accessibilitySelectedRows", "accessibilityDisclosedRows",
                            "accessibilityDisclosureLevel", "accessibilityMinValue", "accessibilityMaxValue" })
                            Require(!Allowed(element, selector), $"{element.AccessibilityRole} advertises {selector}");
                    }
                    Require(!Allowed(Find(root, "command"), "accessibilityValue"), "ordinary button advertises a value");
                    Require(Allowed(Find(root, "label"), "accessibilityValue")
                        && Find(root, "label").AccessibilityValue?.ToString() == "中文说明", "static text lost its readable value");
                    Require(Allowed(Find(root, "command"), "accessibilityPerformPress"), "ordinary button lost its press action");
                    break;
                case 1:
                    var list = new ListBox { Height = 120 };
                    var first = new ListBoxItem { Content = "第一项" };
                    var second = new ListBoxItem { Content = "第二项" };
                    Id(list, "list"); Id(first, "first"); Id(second, "second");
                    list.Items.Add(first); list.Items.Add(second); panel.Children.Add(list); window.UpdateLayout();
                    var axList = Find(root, "list"); var axFirst = Find(root, "first"); var axSecond = Find(root, "second");
                    Require(Allowed(axList, "accessibilitySelectedChildren") && !Allowed(axList, "isAccessibilitySelected"), "selection container advertises item state");
                    Require(Allowed(axFirst, "isAccessibilitySelected") && Allowed(axFirst, "setAccessibilitySelected:")
                        && !axFirst.AccessibilitySelected && !Allowed(axFirst, "isAccessibilityExpanded"), "unselected item lost selection or gained disclosure");
                    axFirst.AccessibilitySelected = true;
                    Require(first.IsSelected && axFirst.AccessibilitySelected
                        && (axList.AccessibilitySelectedChildren ?? []).Any(child => child.Handle == axFirst.Handle), "selection action or container result differs");
                    list.IsEnabled = false;
                    Require(Allowed(axFirst, "isAccessibilitySelected") && !Allowed(axFirst, "setAccessibilitySelected:")
                        && !axSecond.AccessibilityPerformPress() && first.IsSelected, "disabled item loses its readable state or accepts actions");
                    list.IsEnabled = true; axSecond.AccessibilitySelected = true;
                    Require(second.IsSelected && !first.IsSelected, "selection did not restore after enabling");
                    break;
                case 2:
                    var tree = new TreeView { Height = 180 };
                    var branch = new TreeViewItem { Header = "父节点" };
                    var leaf = new TreeViewItem { Header = "叶节点" };
                    Id(tree, "tree"); Id(branch, "branch"); Id(leaf, "leaf");
                    branch.Items.Add(leaf); tree.Items.Add(branch); panel.Children.Add(tree); window.UpdateLayout();
                    var axTree = Find(root, "tree"); var axBranch = Find(root, "branch");
                    Require(Allowed(axTree, "accessibilityRows") && Allowed(axTree, "accessibilitySelectedRows")
                        && !Allowed(axTree, "isAccessibilityDisclosed"), "outline container capabilities differ");
                    Require(Allowed(axBranch, "isAccessibilityExpanded") && Allowed(axBranch, "setAccessibilityDisclosed:")
                        && !axBranch.AccessibilityExpanded, "collapsed branch cannot disclose");
                    axBranch.AccessibilityExpanded = true; window.UpdateLayout();
                    var axLeaf = Find(root, "leaf");
                    Require((axTree.AccessibilityRows ?? []).Length == 2 && axLeaf.AccessibilityDisclosureLevel == 1,
                        "outline row hierarchy differs");
                    Require(Allowed(axLeaf, "isAccessibilitySelected") && !Allowed(axLeaf, "isAccessibilityExpanded")
                        && !Allowed(axLeaf, "isAccessibilityDisclosed") && !Allowed(axLeaf, "setAccessibilityDisclosed:"), "leaf row advertises expandable state");
                    Require(axLeaf.AccessibilityPerformPress() && leaf.IsSelected, "leaf selection action disappeared");
                    break;
                case 3:
                    var title = new TextBlock { Text = "Inputs" };
                    var category = new TextBlock { Text = "控件" };
                    var template = new DataTemplate();
                    template.SetVisualTree(() =>
                    {
                        var content = new StackPanel();
                        content.Children.Add(title); content.Children.Add(category);
                        content.Children.Add(new TextBlock { Text = "模板隐藏内容", Visibility = Visibility.Collapsed });
                        return content;
                    });
                    var item = new ListBoxItem { Content = new CatalogData("textbox", "原始数据内容"), ContentTemplate = template };
                    var itemTemplate = new ControlTemplate(typeof(ListBoxItem));
                    itemTemplate.SetVisualTree(() => new ContentPresenter { Content = item.Content, ContentTemplate = item.ContentTemplate });
                    item.Template = itemTemplate;
                    Id(item, "templated-item"); panel.Children.Add(item); window.UpdateLayout();
                    Require(title.VisualParent != null && title.IsVisible && category.IsVisible,
                        "data template fixture has no realized visible labels");
                    var axItem = Find(root, "templated-item");
                    Require(axItem.AccessibilityLabel == "Inputs 控件", $"templated item exposes data instead of visible labels: {axItem.AccessibilityLabel}");
                    title.Text = "文本输入";
                    Require(axItem.AccessibilityLabel == "文本输入 控件", "template label update left stale accessible name");
                    AutomationProperties.SetLabeledBy(item, label);
                    Require(axItem.AccessibilityLabel == "中文说明", "explicit label relationship was replaced by template data");
                    AutomationProperties.SetName(item, "显式名称");
                    Require(axItem.AccessibilityLabel == "显式名称", "template text overrode explicit name");
                    break;
                case 4:
                    var hidden = new ContentControl();
                    var content = new StackPanel();
                    var inherited = new Button { Content = "继承隐藏", Height = 32 };
                    var revealed = new Button { Content = "显式显示", Height = 32 };
                    Id(hidden, "css-hidden-group"); Id(inherited, "css-hidden-command"); Id(revealed, "css-revealed-command");
                    content.Children.Add(inherited); content.Children.Add(revealed); hidden.Content = content;
                    panel.Children.Add(hidden); window.UpdateLayout();
                    var axInherited = Find(root, "css-hidden-command"); var axRevealed = Find(root, "css-revealed-command");
                    Css.SetStyle(hidden, "visibility: hidden"); Css.SetStyle(revealed, "visibility: visible"); window.UpdateLayout();
                    Require(hidden.Visibility == Visibility.Visible && !hidden.IsVisible && !inherited.IsVisible && revealed.IsVisible,
                        "CSS fixture does not distinguish effective visibility");
                    Require(!Walk(root).Any(element => element.AccessibilityIdentifier is "css-hidden-group" or "css-hidden-command")
                        && !axInherited.AccessibilityElement && !Press(axInherited), "CSS hidden content remains accessible or actionable");
                    Require(Find(root, "css-revealed-command").Handle == axRevealed.Handle && Press(axRevealed),
                        "explicitly visible descendant disappeared beneath a hidden CSS box");
                    Require(axRevealed.AccessibilityParent?.Handle == root.Handle,
                        "visible descendant still points at an inaccessible CSS-hidden parent");
                    hidden.Visibility = Visibility.Collapsed; window.UpdateLayout();
                    Require(!Walk(root).Any(element => element.AccessibilityIdentifier == "css-revealed-command")
                        && !axRevealed.AccessibilityElement, "native collapsed ancestor did not gate CSS-visible descendant");
                    hidden.Visibility = Visibility.Visible; Css.SetStyle(hidden, "visibility: visible"); window.UpdateLayout();
                    Require(Find(root, "css-hidden-command").Handle == axInherited.Handle, "CSS reappearance changed node identity");
                    break;
                case 5:
                    var displayHost = new ContentControl { Content = new Button { Content = "可恢复操作", Height = 32 } };
                    var displayButton = (Button)displayHost.Content;
                    Id(displayHost, "display-host"); Id(displayButton, "display-button"); panel.Children.Add(displayHost); window.UpdateLayout();
                    var axDisplayButton = Find(root, "display-button");
                    Css.SetStyle(displayHost, "display: none"); Css.SetStyle(displayButton, "visibility: visible"); window.UpdateLayout();
                    Require(!displayButton.IsVisible && !Walk(root).Any(element => element.AccessibilityIdentifier == "display-button")
                        && !axDisplayButton.AccessibilityElement && !Press(axDisplayButton), "display:none subtree remains accessible");
                    Css.SetStyle(displayHost, "display: block"); window.UpdateLayout();
                    Require(displayButton.IsVisible && Find(root, "display-button").Handle == axDisplayButton.Handle
                        && Press(axDisplayButton), "display:none reappearance lost identity or actions");
                    break;
                case 6:
                    var multiList = new ListBox { SelectionMode = SelectionMode.Extended, Height = 120 };
                    var multiFirst = new ListBoxItem { Content = "第一项" };
                    var multiSecond = new ListBoxItem { Content = "第二项" };
                    Id(multiFirst, "multi-first"); Id(multiSecond, "multi-second");
                    multiList.Items.Add(multiFirst); multiList.Items.Add(multiSecond); panel.Children.Add(multiList); window.UpdateLayout();
                    var axMultiFirst = Find(root, "multi-first"); var axMultiSecond = Find(root, "multi-second");
                    var secondProvider = (ISelectionItemProvider)multiSecond.GetAutomationPeer()!.GetPattern(PatternInterface.SelectionItem)!;
                    int selectionEvents = 0;
                    multiList.SelectionChanged += (_, _) => selectionEvents++;
                    axMultiFirst.AccessibilitySelected = true;
                    secondProvider.AddToSelection();
                    Require(multiFirst.IsSelected && multiSecond.IsSelected && multiList.SelectedItems.Count == 2
                        && selectionEvents == 2, "add-to-selection did not update the parent list and its event");
                    axMultiSecond.AccessibilitySelected = true;
                    Require(!multiFirst.IsSelected && multiSecond.IsSelected && multiList.SelectedItems.Count == 1
                        && Equals(multiList.SelectedItem, "第二项"), "select action did not replace the list selection");
                    axMultiSecond.AccessibilitySelected = false;
                    Require(!multiSecond.IsSelected && multiList.SelectedItems.Count == 0 && multiList.SelectedItem == null
                        && selectionEvents == 4, "deselect action left a stale collection, binding value or event count");
                    break;
                case 7:
                    var boundList = new ListBox { Height = 120 };
                    var boundFirst = new ListBoxItem { Content = "a" };
                    var boundSecond = new ListBoxItem { Content = "b" };
                    Id(boundFirst, "bound-first"); Id(boundSecond, "bound-second");
                    boundList.Items.Add(boundFirst); boundList.Items.Add(boundSecond);
                    var source = new SelectionSource();
                    BindingOperations.SetBinding(boundList, SelectionControl.SelectedItemProperty, new Binding(nameof(SelectionSource.Item)) { Source = source, Mode = BindingMode.TwoWay });
                    BindingOperations.SetBinding(boundList, SelectionControl.SelectedIndexProperty, new Binding(nameof(SelectionSource.Index)) { Source = source, Mode = BindingMode.TwoWay });
                    BindingOperations.SetBinding(boundList, SelectionControl.SelectedValueProperty, new Binding(nameof(SelectionSource.Value)) { Source = source, Mode = BindingMode.TwoWay });
                    var expression = BindingOperations.GetBindingExpression(boundList, SelectionControl.SelectedItemProperty);
                    panel.Children.Add(boundList); window.UpdateLayout();
                    var axBoundFirst = Find(root, "bound-first"); var axBoundSecond = Find(root, "bound-second");
                    Require(axBoundSecond.AccessibilityPerformPress() && Equals(source.Item, "b") && source.Index == 1 && Equals(source.Value, "b"),
                        "AX selection changed the list but left its two-way source stale");
                    Require(ReferenceEquals(expression, BindingOperations.GetBindingExpression(boundList, SelectionControl.SelectedItemProperty)),
                        "AX selection replaced the binding expression");
                    axBoundFirst.AccessibilitySelected = true;
                    Require(Equals(source.Item, "a") && source.Index == 0 && Equals(source.Value, "a"), "second AX selection did not update the same bindings");
                    BindingOperations.SetBinding(boundList, SelectionControl.SelectedItemProperty, new Binding(nameof(SelectionSource.Item)) { Source = source, Mode = BindingMode.OneWay });
                    axBoundSecond.AccessibilitySelected = true;
                    Require(Equals(source.Item, "a") && boundSecond.IsSelected, "selection wrote into a one-way source");
                    break;
            }
            Console.WriteLine($"PASS: {s_names[index]}"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL: {s_names[index]}: {error}"); return 1; }
        finally { if (window.Handle != 0) window.Close(); }
    }

    private sealed record CatalogData(string Key, string Payload);
    private sealed class SelectionSource
    {
        public object? Item { get; set; }
        public int Index { get; set; } = -1;
        public object? Value { get; set; }
    }
    private static void Id(UIElement element, string value) => AutomationProperties.SetAutomationId(element, value);
    private static bool Allowed(NSAccessibilityElement element, string selector) => element.IsAccessibilitySelectorAllowed(new Selector(selector));
    private static NSAccessibilityElement Find(NSAccessibilityElement root, string id) => Walk(root).Single(element => element.AccessibilityIdentifier == id);
    private static IEnumerable<NSAccessibilityElement> Walk(NSAccessibilityElement root)
    {
        yield return root;
        foreach (var child in (root.AccessibilityChildren ?? []).OfType<NSAccessibilityElement>())
            foreach (var element in Walk(child)) yield return element;
    }
    private static bool Press(NSAccessibilityElement element)
    {
        bool accepted = element.AccessibilityPerformPress();
        Jalium.UI.Threading.Dispatcher.GetForCurrentThread().ProcessQueue();
        return accepted;
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
