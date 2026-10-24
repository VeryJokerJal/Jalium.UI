using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class SelectionTransactionBoundaryTests
{
    public static IEnumerable<object[]> AddShapes()
    {
        foreach (SelectionMode mode in new[] { SelectionMode.Multiple, SelectionMode.Extended })
        foreach (bool source in new[] { false, true })
        foreach (int operation in new[] { 0, 1, 2, 3 }) yield return [mode, source, operation];
    }

    [Theory, MemberData(nameof(AddShapes))]
    public void PublicCollectionAddsAnUnselectedRepeatedOccurrence(SelectionMode mode, bool source, int operation)
    {
        object item = new Row(7); var list = new ListBox { SelectionMode = mode };
        var items = new ObservableCollection<object> { item, item, new Row(9) };
        if (source) list.ItemsSource = items; else foreach (object value in items) list.Items.Add(value);
        var peer = Assert.IsType<ListBoxAutomationPeer>(list.GetAutomationPeer()); var rows = peer.GetChildren()!;
        int notifications = 0; ((System.Collections.Specialized.INotifyCollectionChanged)list.SelectedItems).CollectionChanged += (_, _) => notifications++;
        int Add()
        {
            if (operation == 0) return list.SelectedItems.Add(item);
            if (operation == 1) list.SelectedItems.Insert(0, item);
            else if (operation == 2) ((IList<object>)list.SelectedItems).Add(item);
            else ((IList<object>)list.SelectedItems).Insert(list.SelectedItems.Count, item);
            return -1;
        }
        int first = Add(), second = Add();
        Assert.Equal(2, list.SelectedItems.Count); Assert.Equal(new[] { rows[0], rows[1] }, peer.GetSelection());
        Assert.Same(item, list.SelectedItems[0]); Assert.Same(item, list.SelectedItems[1]);
        if (operation == 0) { Assert.Equal(0, first); Assert.Equal(1, second); }
        Assert.Equal(2, notifications); Add(); Assert.Equal(2, list.SelectedItems.Count); Assert.Equal(2, notifications);
        list.SelectedItems.RemoveAt(0); Assert.Single(peer.GetSelection());
        Assert.Same(rows[operation == 1 ? 0 : 1], Assert.Single(peer.GetSelection()));
        Add(); Assert.Equal(new[] { rows[0], rows[1] }, peer.GetSelection()); Assert.Equal(4, notifications);
    }

    [Fact]
    public void SingleSelectionAddingTheAlreadySelectedReferenceRemainsANoOp()
    {
        var item = new Row(7); var list = new ListBox(); list.Items.Add(item); list.Items.Add(item);
        list.SelectedIndex = 1; Assert.Equal(0, list.SelectedItems.Add(item));
        Assert.Single(list.SelectedItems); Assert.Equal(1, list.SelectedIndex);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void GeneratedComboContainersFollowTheSelectedRepeatedRow(bool source)
    {
        var item = new Row(7); var combo = new ComboBox();
        if (source) combo.ItemsSource = new ObservableCollection<object> { item, item };
        else { combo.Items.Add(item); combo.Items.Add(item); }
        var generator = combo.ItemContainerGenerator;
        using (generator.StartAt(generator.GeneratorPositionFromIndex(0), GeneratorDirection.Forward, true))
        {
            Assert.IsType<ComboBoxItem>(generator.GenerateNext(out _));
            Assert.IsType<ComboBoxItem>(generator.GenerateNext(out _));
        }
        var first = Assert.IsType<ComboBoxItem>(generator.ContainerFromIndex(0));
        var second = Assert.IsType<ComboBoxItem>(generator.ContainerFromIndex(1)); Assert.NotSame(first, second);
        combo.SelectedIndex = 0; Assert.True(first.IsSelected); Assert.False(second.IsSelected);
        combo.SelectedIndex = 1; Assert.False(first.IsSelected); Assert.True(second.IsSelected);
        combo.SelectedIndex = -1; Assert.False(first.IsSelected); Assert.False(second.IsSelected);
    }

    [Theory, InlineData(false, false), InlineData(false, true), InlineData(true, false), InlineData(true, true)]
    public void PreparingAPreselectedComboUsesTheContainerRow(bool source, bool repeatedReference)
    {
        var firstItem = new Row(7); var secondItem = repeatedReference ? firstItem : new Row(7); var combo = new ComboBox();
        if (source) combo.ItemsSource = new ObservableCollection<object> { firstItem, secondItem };
        else { combo.Items.Add(firstItem); combo.Items.Add(secondItem); }
        combo.SelectedIndex = 1;
        var generator = combo.ItemContainerGenerator;
        using (generator.StartAt(generator.GeneratorPositionFromIndex(0), GeneratorDirection.Forward, true))
        {
            var first = Assert.IsType<ComboBoxItem>(generator.GenerateNext(out _)); generator.PrepareItemContainer(first);
            var second = Assert.IsType<ComboBoxItem>(generator.GenerateNext(out _)); generator.PrepareItemContainer(second);
            Assert.False(first.IsSelected); Assert.True(second.IsSelected);
        }
    }

    public static IEnumerable<object[]> ReentryShapes()
    {
        foreach (bool combo in new[] { false, true }) foreach (int origin in new[] { 0, 1, 2 })
        foreach (int hook in new[] { 0, 1, 2, 3 }) foreach (int requested in new[] { 0, 1, 2 })
            yield return [combo, origin, hook, requested];
    }

    [Theory, MemberData(nameof(ReentryShapes))]
    public void ANewSelectionFromAScalarOrContainerCallbackRemainsCoherent(bool combo, int origin, int hook, int requested)
    {
        var control = Create(combo); var probe = (IProbe)control; var rows = Fill(control); bool invoked = false;
        control.SelectionChanged += (_, _) => AssertCoherent(control, rows);
        probe.Callback = property =>
        {
            if (invoked || property != Property(hook)) return;
            invoked = true; Choose(control, rows, requested, 2);
        };
        Choose(control, rows, origin, 1); Assert.True(invoked);
        AssertSelection(control, rows, 2); Assert.Equal(2, probe.PaintedIndex); Assert.Same(rows[2], probe.PaintedItem);
    }

    [Theory, InlineData(false, 0), InlineData(false, 1), InlineData(false, 2), InlineData(true, 0), InlineData(true, 1), InlineData(true, 2)]
    public void AThrowingCallbackRetainsItsNewSelectionAndTheFirstException(bool combo, int requested)
    {
        var control = Create(combo); var probe = (IProbe)control; var rows = Fill(control); bool invoked = false;
        var expected = new MarkerException();
        probe.Callback = property =>
        {
            if (invoked || property != Selector.SelectedItemProperty) return;
            invoked = true; Choose(control, rows, requested, 2); throw expected;
        };
        Assert.Same(expected, Assert.Throws<MarkerException>(() => control.SelectedIndex = 1)); Assert.True(invoked);
        AssertSelection(control, rows, 2); probe.Callback = null; control.SelectedIndex = 3; AssertSelection(control, rows, 3);
    }

    [Theory, InlineData(false, false), InlineData(false, true), InlineData(true, false), InlineData(true, true)]
    public void SelectedValueBindingSurvivesAnIndexSelection(bool combo, bool twoWay)
    {
        var control = Create(combo); var rows = Fill(control); var source = new ChoiceSource { Value = 1 };
        control.SetBinding(Selector.SelectedValueProperty, new Binding(nameof(ChoiceSource.Value)) { Source = source, Mode = twoWay ? BindingMode.TwoWay : BindingMode.OneWay });
        var expression = control.GetBindingExpression(Selector.SelectedValueProperty); Assert.NotNull(expression);
        control.SelectedIndex = 1; AssertSelection(control, rows, 1);
        Assert.Same(expression, control.GetBindingExpression(Selector.SelectedValueProperty)); Assert.Equal(twoWay ? 2 : 1, source.Value);
        source.Value = 3; AssertSelection(control, rows, 2);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void AValueGetterCannotPublishItsOlderSelectionAfterChoosingANewerRow(bool combo)
    {
        var control = Create(combo); var rows = new[] { new GetterRow(1), new GetterRow(2), new GetterRow(3) };
        foreach (var row in rows) control.Items.Add(row); control.SelectedValuePath = nameof(GetterRow.Key); control.SelectedIndex = 0;
        bool invoked = false; rows[1].Read = () => { if (!invoked) { invoked = true; control.SelectedIndex = 2; } };
        control.SelectedIndex = 1; Assert.True(invoked); Assert.Equal(2, control.SelectedIndex);
        Assert.Same(rows[2], control.SelectedItem); Assert.Equal(3, control.SelectedValue); Assert.Equal(2, ((IProbe)control).PaintedIndex);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void ASubclassMayChooseANewerItemBeforeCallingTheBaseNotification(bool combo)
    {
        var control = Create(combo); var probe = (IProbe)control; var rows = Fill(control); bool invoked = false; probe.BeforeBase = true;
        probe.Callback = property => { if (!invoked && property == Selector.SelectedItemProperty) { invoked = true; control.SelectedItem = rows[2]; } };
        control.SelectedIndex = 1; Assert.True(invoked); AssertSelection(control, rows, 2);
    }

    private static DependencyProperty? Property(int value) => value switch { 0 => Selector.SelectedIndexProperty, 1 => Selector.SelectedItemProperty, 2 => Selector.SelectedValueProperty, _ => null };
    private static Selector Create(bool combo) => combo ? new ProbeCombo() : new ProbeSelector();
    private static Row[] Fill(Selector control)
    {
        var rows = new[] { new Row(1), new Row(2), new Row(3), new Row(4) };
        foreach (var row in rows) control.Items.Add(row); control.SelectedValuePath = nameof(Row.Key); control.SelectedIndex = 0; return rows;
    }
    private static void Choose(Selector control, Row[] rows, int property, int index)
    { if (property == 0) control.SelectedIndex = index; else if (property == 1) control.SelectedItem = rows[index]; else control.SelectedValue = rows[index].Key; }
    private static void AssertSelection(Selector control, Row[] rows, int index)
    { Assert.Equal(index, control.SelectedIndex); Assert.Same(rows[index], control.SelectedItem); Assert.Equal(rows[index].Key, control.SelectedValue); }
    private static void AssertCoherent(Selector control, Row[] rows) => AssertSelection(control, rows, control.SelectedIndex);
    private interface IProbe { bool BeforeBase { get; set; } Action<DependencyProperty?>? Callback { get; set; } int PaintedIndex { get; } object? PaintedItem { get; } }
    private sealed class ProbeSelector : Selector, IProbe
    {
        public bool BeforeBase { get; set; }
        public Action<DependencyProperty?>? Callback { get; set; }
        public int PaintedIndex { get; private set; } public object? PaintedItem { get; private set; }
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) { if (BeforeBase) Callback?.Invoke(e.Property); base.OnPropertyChanged(e); if (!BeforeBase) Callback?.Invoke(e.Property); }
        protected override void UpdateContainerSelection() { base.UpdateContainerSelection(); PaintedIndex = SelectedIndex; PaintedItem = SelectedItem; Callback?.Invoke(null); }
    }
    private sealed class ProbeCombo : ComboBox, IProbe
    {
        public bool BeforeBase { get; set; }
        public Action<DependencyProperty?>? Callback { get; set; }
        public int PaintedIndex { get; private set; } public object? PaintedItem { get; private set; }
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) { if (BeforeBase) Callback?.Invoke(e.Property); base.OnPropertyChanged(e); if (!BeforeBase) Callback?.Invoke(e.Property); }
        protected override void UpdateContainerSelection() { base.UpdateContainerSelection(); PaintedIndex = SelectedIndex; PaintedItem = SelectedItem; Callback?.Invoke(null); }
    }
    private sealed record Row(int Key) { public override string ToString() => "重复项🙂 Miii"; }
    private sealed class GetterRow(int key)
    {
        internal Action? Read;
        public int Key { get { Read?.Invoke(); return key; } }
    }
    private sealed class MarkerException : Exception;
    private sealed class ChoiceSource : INotifyPropertyChanged
    {
        private int _value;
        public int Value { get => _value; set { if (_value == value) return; _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
