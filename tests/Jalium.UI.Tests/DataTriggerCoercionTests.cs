using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// Regression for: DataTrigger / MultiDataTrigger compared the (XAML-authored) string Value against
/// the binding's runtime value WITHOUT type coercion, so Value="True" never matched a bool binding
/// (e.g. IsMouseOver) and the trigger silently never activated. The property-based Trigger /
/// MultiTrigger already coerced via ConvertValueIfNeeded(Value, Property.PropertyType); the binding
/// triggers now coerce against the binding result's runtime type. Uses TriggerTestExtensions
/// (AttachForTest) declared in MultiDataTriggerTests.cs.
/// </summary>
public class DataTriggerCoercionTests
{
    [Fact]
    public void DataTrigger_StringTrue_CoercesToBoolBinding_Activates()
    {
        var vm = new Vm { Flag = true };
        var element = new El { DataContext = vm };
        var trigger = new DataTrigger { Binding = new Binding("Flag"), Value = "True" };
        trigger.Setters.Add(new Setter(El.MarkProperty, 0.5));

        trigger.AttachForTest(element);

        Assert.True(trigger.IsActiveForElement(element));
        Assert.Equal(0.5, element.GetValue(El.MarkProperty));
    }

    [Fact]
    public void DataTrigger_StringTrue_BoolFalse_DoesNotActivate()
    {
        var vm = new Vm { Flag = false };
        var element = new El { DataContext = vm };
        var trigger = new DataTrigger { Binding = new Binding("Flag"), Value = "True" };
        trigger.Setters.Add(new Setter(El.MarkProperty, 0.5));

        trigger.AttachForTest(element);

        Assert.False(trigger.IsActiveForElement(element));
        Assert.Equal(1.0, element.GetValue(El.MarkProperty)); // default
    }

    [Fact]
    public void DataTrigger_StringTrue_ReactsToBoolChange()
    {
        var vm = new Vm { Flag = false };
        var element = new El { DataContext = vm };
        var trigger = new DataTrigger { Binding = new Binding("Flag"), Value = "True" };
        trigger.Setters.Add(new Setter(El.MarkProperty, 0.5));

        trigger.AttachForTest(element);
        Assert.False(trigger.IsActiveForElement(element));

        vm.Flag = true; // hover-equivalent transition

        Assert.True(trigger.IsActiveForElement(element));
        Assert.Equal(0.5, element.GetValue(El.MarkProperty));
    }

    [Fact]
    public void DataTrigger_StringEnum_CoercesToEnumBinding_Activates()
    {
        var vm = new Vm { Vis = Visibility.Collapsed };
        var element = new El { DataContext = vm };
        var trigger = new DataTrigger { Binding = new Binding("Vis"), Value = "Collapsed" };
        trigger.Setters.Add(new Setter(El.MarkProperty, 0.5));

        trigger.AttachForTest(element);

        Assert.True(trigger.IsActiveForElement(element));
    }

    [Fact]
    public void MultiDataTrigger_StringTrue_CoercesToBoolBinding_Activates()
    {
        var vm = new Vm { Flag = true };
        var element = new El { DataContext = vm };
        var trigger = new MultiDataTrigger();
        trigger.Conditions.Add(new BindingCondition { Binding = new Binding("Flag"), Value = "True" });
        trigger.Setters.Add(new Setter(El.MarkProperty, 0.5));

        trigger.AttachForTest(element);

        Assert.True(trigger.IsActiveForElement(element));
        Assert.Equal(0.5, element.GetValue(El.MarkProperty));
    }

    [Fact]
    public void DataTrigger_RelativeSourceAncestor_IsMouseOver_ActivatesWhenAncestorHovered()
    {
        // The exact hover mechanism: a child's DataTrigger reads the ancestor's IsMouseOver via
        // RelativeSource AncestorType, with a XAML-style string Value="True". Proves the full chain
        // (ancestor resolution + DP-change subscription + string→bool coercion) end to end.
        var ancestor = new Grid();
        var child = new El();
        ancestor.Children.Add(child); // establish visual-tree parent so FindAncestor resolves

        var trigger = new DataTrigger
        {
            Binding = new Binding(nameof(UIElement.IsMouseOver))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(Grid) }
            },
            Value = "True"
        };
        trigger.Setters.Add(new Setter(El.MarkProperty, 0.5));
        trigger.AttachForTest(child);

        Assert.False(trigger.IsActiveForElement(child)); // ancestor not hovered

        ancestor.SetIsMouseOver(true);
        Assert.True(trigger.IsActiveForElement(child));  // resolves ancestor + reacts + coerces
        Assert.Equal(0.5, child.GetValue(El.MarkProperty));

        ancestor.SetIsMouseOver(false);
        Assert.False(trigger.IsActiveForElement(child)); // deactivates on leave
    }

    [Fact]
    public void SingleBorder_HoverAndSelected_SwapAndRevert_AcrossAllTransitions()
    {
        // Models the template card's "single outer border, no inner ring" hover: a Style with a
        // baseline BorderBrush, a hover MultiDataTrigger (Selected=False AND Hovered=True), and a
        // selected DataTrigger (Selected=True). Asserts the brush both SWAPS and REVERTS across
        // every transition — i.e. the alleged "Brush trigger never reverts" residue bug is gone.
        var baseBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x4B, 0x40));   // WelcomeBorder
        var glowBrush = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));   // hover
        var accentBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // selected

        var vm = new Vm { Selected = false, Hovered = false };
        var element = new El { DataContext = vm };

        var style = new Style(typeof(El));
        style.Setters.Add(new Setter(El.EdgeProperty, baseBrush)); // baseline

        var hover = new MultiDataTrigger();
        hover.Conditions.Add(new BindingCondition { Binding = new Binding("Selected"), Value = "False" });
        hover.Conditions.Add(new BindingCondition { Binding = new Binding("Hovered"), Value = "True" });
        hover.Setters.Add(new Setter(El.EdgeProperty, glowBrush));
        style.Triggers.Add(hover);

        var selected = new DataTrigger { Binding = new Binding("Selected"), Value = "True" };
        selected.Setters.Add(new Setter(El.EdgeProperty, accentBrush));
        style.Triggers.Add(selected);

        element.Style = style;

        Assert.Same(baseBrush, element.GetValue(El.EdgeProperty));   // resting
        vm.Hovered = true;
        Assert.Same(glowBrush, element.GetValue(El.EdgeProperty));   // hover (unselected)
        vm.Hovered = false;
        Assert.Same(baseBrush, element.GetValue(El.EdgeProperty));   // revert to baseline (the bug)
        vm.Selected = true;
        Assert.Same(accentBrush, element.GetValue(El.EdgeProperty)); // selected
        vm.Hovered = true;
        Assert.Same(accentBrush, element.GetValue(El.EdgeProperty)); // hover suppressed while selected
        vm.Selected = false;
        Assert.Same(glowBrush, element.GetValue(El.EdgeProperty));   // hover takes over on deselect
        vm.Hovered = false;
        Assert.Same(baseBrush, element.GetValue(El.EdgeProperty));   // back to baseline
    }

    [Fact]
    public void TwoDataTriggers_CardTransitions_SwapAndRevertCorrectly()
    {
        // The view uses two single DataTriggers on the SAME outer-border BorderBrush (MultiDataTrigger
        // is omitted from the source generator's type map, so it can't be used in markup). Hover is
        // declared first, Selected last, so Selected wins when both are active (later-sibling
        // precedence via the StyleTrigger layer's last-writer). This covers every transition the
        // single-select template picker can actually produce.
        //
        // NOT relied upon: deselecting a card while the pointer still sits on it (would need the
        // earlier "hover" trigger to re-apply when "selected" deactivates). That path is unreachable
        // in this picker — selecting another card moves the pointer off — and the framework's
        // Style.Triggers sibling takeover has a separate latent bug there. Final state below sets
        // both flags false (the picker's real deselect), which correctly resolves to baseline.
        var baseBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x4B, 0x40));
        var glowBrush = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
        var accentBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));

        var vm = new Vm { Selected = false, Hovered = false };
        var element = new El { DataContext = vm };

        var style = new Style(typeof(El));
        style.Setters.Add(new Setter(El.EdgeProperty, baseBrush));

        var hover = new DataTrigger { Binding = new Binding("Hovered"), Value = "True" }; // declared first
        hover.Setters.Add(new Setter(El.EdgeProperty, glowBrush));
        style.Triggers.Add(hover);

        var selected = new DataTrigger { Binding = new Binding("Selected"), Value = "True" }; // declared last → wins
        selected.Setters.Add(new Setter(El.EdgeProperty, accentBrush));
        style.Triggers.Add(selected);

        element.Style = style;

        Assert.Same(baseBrush, element.GetValue(El.EdgeProperty));   // resting
        vm.Hovered = true;
        Assert.Same(glowBrush, element.GetValue(El.EdgeProperty));   // hover (unselected)
        vm.Hovered = false;
        Assert.Same(baseBrush, element.GetValue(El.EdgeProperty));   // mouse leave -> baseline
        vm.Selected = true;
        Assert.Same(accentBrush, element.GetValue(El.EdgeProperty)); // selected
        vm.Hovered = true;
        Assert.Same(accentBrush, element.GetValue(El.EdgeProperty)); // hover while selected -> selected wins
        vm.Selected = false;
        vm.Hovered = false;
        Assert.Same(baseBrush, element.GetValue(El.EdgeProperty));   // deselect with pointer off -> baseline
    }

    private class El : FrameworkElement
    {
        public static readonly DependencyProperty MarkProperty =
            DependencyProperty.Register("Mark", typeof(double), typeof(El), new PropertyMetadata(1.0));

        public static readonly DependencyProperty EdgeProperty =
            DependencyProperty.Register("Edge", typeof(Brush), typeof(El), new PropertyMetadata(null));
    }

    private class Vm : INotifyPropertyChanged
    {
        private bool _flag;
        private bool _selected;
        private bool _hovered;
        private Visibility _vis = Visibility.Visible;

        public bool Flag
        {
            get => _flag;
            set { if (_flag != value) { _flag = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Flag))); } }
        }

        public bool Selected
        {
            get => _selected;
            set { if (_selected != value) { _selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); } }
        }

        public bool Hovered
        {
            get => _hovered;
            set { if (_hovered != value) { _hovered = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Hovered))); } }
        }

        public Visibility Vis
        {
            get => _vis;
            set { if (_vis != value) { _vis = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Vis))); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
