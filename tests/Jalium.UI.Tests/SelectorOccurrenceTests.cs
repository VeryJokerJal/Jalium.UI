using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class SelectorOccurrenceTests
{
    public static IEnumerable<object[]> Shapes()
    { foreach (bool combo in new[] { false, true }) foreach (int kind in new[] { 0, 1, 2 }) yield return [combo, kind]; }

    [Theory, MemberData(nameof(Shapes))]
    public void RemovingAnEqualEarlierOccurrenceKeepsTheSelectedObject(bool combo, int kind)
    {
        var f = new Fixture(combo, kind); f.Select(1); int changes = 0; f.Control.SelectionChanged += (_, _) => changes++;
        f.Source.RemoveAt(0); f.AssertSelection(0, f.Second); Assert.Equal(0, changes);
    }

    [Theory, MemberData(nameof(Shapes))]
    public void RemovingTheSelectedOccurrenceClearsInsteadOfSelectingItsEqualSibling(bool combo, int kind)
    { var f = new Fixture(combo, kind); f.Select(1); f.Source.RemoveAt(1); f.AssertSelection(-1, null); }

    [Theory, MemberData(nameof(Shapes))]
    public void ReplacingAnEqualEarlierOccurrenceDoesNotRetargetSelection(bool combo, int kind)
    {
        var f = new Fixture(combo, kind); f.Select(1); int changes = 0; f.Control.SelectionChanged += (_, _) => changes++;
        f.Source[0] = new Row(30); f.AssertSelection(1, f.Second); Assert.Equal(0, changes);
    }

    [Theory, MemberData(nameof(Shapes))]
    public void ReplacingTheSelectedOccurrenceKeepsItsSlotEvenForAnEqualReplacement(bool combo, int kind)
    {
        var f = new Fixture(combo, kind); f.Select(1); object next = kind == 2 ? new EqualContainer { Content = "新同值项" } : new Row(7);
        f.Source[1] = next; f.AssertSelection(1, next);
    }

    [Theory, MemberData(nameof(Shapes))]
    public void MovingASelectedOccurrenceRetainsItsOwnRow(bool combo, int kind)
    { var f = new Fixture(combo, kind); f.Select(1); f.Source.Move(1, 0); f.AssertSelection(0, f.Second); f.Source.Move(0, 2); f.AssertSelection(2, f.Second); }

    [Theory, MemberData(nameof(Shapes))]
    public void MovingAnotherOccurrenceAcrossSelectionOnlyShiftsItsIndex(bool combo, int kind)
    { var f = new Fixture(combo, kind); f.Select(1); f.Source.Move(0, 2); f.AssertSelection(0, f.Second); }

    [Theory, MemberData(nameof(Shapes))]
    public void RangeRemovalBeforeSelectionPreservesTheSurvivingReference(bool combo, int kind)
    { var f = new Fixture(combo, kind); f.Select(2); f.Source.RemoveRange(0, 2); f.AssertSelection(0, f.Third); }

    [Theory, MemberData(nameof(Shapes))]
    public void UnequalLengthReplacementBeforeSelectionShiftsTheSurvivingIndex(bool combo, int kind)
    { var f = new Fixture(combo, kind); f.Select(2); f.Source.ReplaceRange(0, 2, new Row(20)); f.AssertSelection(1, f.Third); }

    [Theory, MemberData(nameof(Shapes))]
    public void ResetFindsTheSurvivingReferenceAtItsNewIndex(bool combo, int kind)
    { var f = new Fixture(combo, kind); f.Select(2); f.Source.ResetTo(f.Third, f.First, f.Second); f.AssertSelection(0, f.Third); }

    [Theory, MemberData(nameof(Shapes))]
    public void ResetDoesNotSubstituteAnotherEqualObjectForTheRemovedSelection(bool combo, int kind)
    {
        var f = new Fixture(combo, kind); f.Select(1); f.Source.ResetTo(kind == 2 ? new EqualContainer { Content = "同值新项" } : new Row(7));
        f.AssertSelection(-1, null);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void SelectedItemPrefersAnExactReferenceOverAnEarlierEqualValue(bool combo)
    { var f = new Fixture(combo, 0); f.Control.SelectedItem = f.Second; f.AssertSelection(1, f.Second); }

    [Theory, InlineData(false), InlineData(true)]
    public void ResetRetainsTheSelectedRepeatedReferenceOrdinalWhenItStillExists(bool combo)
    { var f = new Fixture(combo, 1); f.Select(1); f.Source.ResetTo(f.First, f.Second, f.Third); f.AssertSelection(1, f.Second); }

    [Theory, InlineData(false), InlineData(true)]
    public void PropertyCallbackThatMutatesTheSourceCannotLeaveStaleScalarSelection(bool itemProperty)
    {
        var control = new CallbackSelector(); var one = new Row(1); var two = new Row(2); var source = new RangeSource { one, two };
        control.ItemsSource = source; bool changed = false;
        control.Callback = e => { if (!changed && e.Property == (itemProperty ? Selector.SelectedItemProperty : Selector.SelectedIndexProperty) && control.SelectedIndex == 1)
            { changed = true; source.RemoveAt(0); } };
        control.SelectedIndex = 1; Assert.True(changed); Assert.Equal(0, control.SelectedIndex); Assert.Same(two, control.SelectedItem);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void SelectionChangedCallbackCanShiftTheNewlySelectedOccurrence(bool combo)
    {
        var f = new Fixture(combo, 0); bool changed = false;
        f.Control.SelectionChanged += (_, _) => { if (!changed && ReferenceEquals(f.Control.SelectedItem, f.Second)) { changed = true; f.Source.RemoveAt(0); } };
        f.Control.SelectedIndex = 1; Assert.True(changed); f.AssertSelection(0, f.Second);
    }

    private sealed class Fixture
    {
        internal readonly Selector Control;
        internal readonly RangeSource Source;
        internal readonly object First, Second, Third = new Row(9);
        internal Fixture(bool combo, int kind)
        {
            Control = combo ? new ComboBox() : new CallbackSelector();
            First = kind == 2 ? new EqualContainer { Content = "同值项" } : new Row(7);
            Second = kind == 1 ? First : kind == 2 ? new EqualContainer { Content = "同值项" } : new Row(7);
            Source = new RangeSource { First, Second, Third }; Control.ItemsSource = Source;
        }
        internal void Select(int index) { Control.SelectedIndex = index; AssertSelection(index, Source[index]); }
        internal void AssertSelection(int index, object? item) { Assert.Equal(index, Control.SelectedIndex); Assert.Same(item, Control.SelectedItem); }
    }
    private sealed class CallbackSelector : Selector
    {
        internal Action<DependencyPropertyChangedEventArgs>? Callback;
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) { base.OnPropertyChanged(e); Callback?.Invoke(e); }
    }
    private sealed record Row(int Key) { public override string ToString() => "同值项🙂"; }
    private sealed class EqualContainer : ComboBoxItem
    { public override bool Equals(object? other) => other is EqualContainer; public override int GetHashCode() => 7; }
    private sealed class RangeSource : ObservableCollection<object>
    {
        internal void RemoveRange(int index, int count)
        { var old = this.Skip(index).Take(count).ToArray(); for (int i = 0; i < count; i++) Items.RemoveAt(index); OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, old, index)); }
        internal void ReplaceRange(int index, int count, params object[] next)
        { var old = this.Skip(index).Take(count).ToArray(); for (int i = 0; i < count; i++) Items.RemoveAt(index); for (int i = 0; i < next.Length; i++) Items.Insert(index + i, next[i]); OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, next, old, index)); }
        internal void ResetTo(params object[] next)
        { Items.Clear(); foreach (var item in next) Items.Add(item); OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)); }
    }
}
