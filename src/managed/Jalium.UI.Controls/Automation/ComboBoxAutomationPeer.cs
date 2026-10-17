using Jalium.UI.Controls;

namespace Jalium.UI.Automation.Peers;

public partial class ComboBoxAutomationPeer
{
    internal string ItemText(object item, bool semanticName)
    {
        var container = item as DependencyObject
            ?? ComboBoxOwner.ItemContainerGenerator.ContainerFromItem(item);
        if (semanticName && container is not null
            && AutomationProperties.GetName(container) is { Length: > 0 } name) return name;
        if (item is ContentControl { Content: UIElement visual })
        {
            if (TextSearch.GetText(item as DependencyObject ?? visual) is { Length: > 0 } search) return search;
            return visual switch
            {
                TextBlock text => text.Text ?? string.Empty,
                AccessText text => text.Text ?? string.Empty,
                TextBox text => text.Text ?? string.Empty,
                _ => visual.GetAutomationPeer()?.GetName() ?? ComboBoxOwner.GetItemText(item),
            };
        }
        return ComboBoxOwner.GetItemText(item);
    }

    private sealed class ComboBoxDataItemAutomationPeer(object item, ComboBoxAutomationPeer parent)
        : SelectorItemAutomationPeer(item, parent)
    {
        private ComboBox Combo => parent.ComboBoxOwner;
        public override bool IsSelected => Combo.SelectedIndex >= 0 && Combo.SelectedIndex < Combo.Items.Count
            && Equals(Combo.Items[Combo.SelectedIndex], Item);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
        protected override string GetNameCore() => parent.ItemText(Item, true);
        protected override bool IsEnabledCore() => Combo.IsEnabled && base.IsEnabledCore();
        public override void Select() => Combo.SetAutomationSelection(Item, true);
        public override void AddToSelection() => Combo.SetAutomationSelection(Item, true);
        public override void RemoveFromSelection() => Combo.SetAutomationSelection(Item, false);
    }
}
