using System.Collections.ObjectModel;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class ComboBoxOccurrenceAutomationTests
{
    public static IEnumerable<object[]> Shapes()
    {
        foreach (int kind in new[] { 0, 1, 2 })
        foreach (bool source in new[] { false, true })
        foreach (bool realized in new[] { false, true }) yield return [kind, source, realized];
    }

    [Theory, MemberData(nameof(Shapes))]
    public void SelectionStateIdentifiesOnlyTheChosenRow(int kind, bool source, bool realized)
    {
        var f = new Fixture(kind, source, realized); var rows = f.Rows();
        foreach (int index in new[] { 1, 0, 2, -1 }) { f.Combo.SelectedIndex = index; f.AssertSelection(rows, index); }
    }

    public static IEnumerable<object[]> Actions()
    {
        foreach (var shape in Shapes()) foreach (int action in new[] { 0, 1, 2 }) yield return [.. shape, action];
    }

    [Theory, MemberData(nameof(Actions))]
    public void ASelectionActionTargetsItsOwnOccurrence(int kind, bool source, bool realized, int action)
    {
        var f = new Fixture(kind, source, realized); var rows = f.Rows();
        f.Combo.SelectedIndex = action == 2 ? 1 : -1;
        if (action == 0) rows[1].Select(); else if (action == 1) rows[1].AddToSelection(); else rows[1].RemoveFromSelection();
        Assert.Equal(action == 2 ? -1 : 1, f.Combo.SelectedIndex);
        f.AssertSelection(rows, action == 2 ? -1 : 1);
    }

    [Theory, MemberData(nameof(Shapes))]
    public void RemovingAnUnselectedEqualRowPreservesTheSelectedOccurrence(int kind, bool source, bool realized)
    {
        var f = new Fixture(kind, source, realized); var rows = f.Rows(); f.Combo.SelectedIndex = 1;
        rows[0].RemoveFromSelection(); Assert.Equal(1, f.Combo.SelectedIndex); f.AssertSelection(rows, 1);
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void APeerTracksItsRowAcrossInsertAndMove(int kind)
    {
        var f = new Fixture(kind, true, true); var rows = f.Rows(); var second = rows[1]; f.Combo.SelectedIndex = 1;
        f.Source.Insert(0, new Row(11)); Assert.Same(second, f.Rows()[2]); Assert.Equal(2, f.Combo.SelectedIndex);
        f.Combo.SelectedIndex = -1; second.Select(); Assert.Equal(2, f.Combo.SelectedIndex);
        f.Source.Move(2, 0); Assert.Same(second, f.Rows()[0]); Assert.Equal(0, f.Combo.SelectedIndex);
        f.Combo.SelectedIndex = -1; second.AddToSelection(); Assert.Equal(0, f.Combo.SelectedIndex);
        second.RemoveFromSelection(); Assert.Equal(-1, f.Combo.SelectedIndex);
    }

    [Theory, InlineData(0, 0), InlineData(0, 1), InlineData(0, 2), InlineData(1, 0), InlineData(1, 1), InlineData(1, 2), InlineData(2, 0), InlineData(2, 1), InlineData(2, 2)]
    public void ARemovedReplacedOrResetPeerCannotSelectAnotherEqualRow(int kind, int mutation)
    {
        var f = new Fixture(kind, true, true); var old = f.Rows()[1]; f.Combo.SelectedIndex = 1;
        if (mutation == 0) f.Source.RemoveAt(1);
        else if (mutation == 1) f.Source[1] = kind == 2 ? new EqualContainer(7) : new Row(7);
        else { f.Source.Clear(); f.Source.Add(f.First); f.Source.Add(f.Second); }
        int selected = f.Combo.SelectedIndex; Assert.False(old.IsSelected);
        Assert.Throws<InvalidOperationException>(() => old.Select()); Assert.Throws<InvalidOperationException>(() => old.AddToSelection()); Assert.Throws<InvalidOperationException>(() => old.RemoveFromSelection());
        Assert.Equal(selected, f.Combo.SelectedIndex); Assert.DoesNotContain(old, f.Rows());
    }

    [Theory, InlineData(0, false), InlineData(0, true), InlineData(1, false), InlineData(1, true), InlineData(2, false), InlineData(2, true)]
    public void DisabledOwnerOrRowRejectsActionsWithoutRetargetingItsEqualSibling(int kind, bool owner)
    {
        var f = new Fixture(kind, true, true); var rows = f.Rows(); f.Combo.SelectedIndex = 0;
        if (owner) f.Combo.IsEnabled = false; else f.Container(1).IsEnabled = false;
        Assert.Throws<InvalidOperationException>(() => rows[1].Select()); Assert.Throws<InvalidOperationException>(() => rows[1].AddToSelection()); Assert.Throws<InvalidOperationException>(() => rows[1].RemoveFromSelection());
        Assert.Equal(0, f.Combo.SelectedIndex);
    }

    [Theory, InlineData(0, false), InlineData(0, true), InlineData(1, false), InlineData(1, true), InlineData(2, false), InlineData(2, true)]
    public void DirectContainerProvidersKeepTheirExistingSelectionPath(int kind, bool source)
    {
        var f = new Fixture(kind, source, true); var row = Assert.IsAssignableFrom<ISelectionItemProvider>(f.Container(1).GetAutomationPeer());
        row.Select(); Assert.Equal(1, f.Combo.SelectedIndex); Assert.False(f.Container(0).IsSelected); Assert.True(f.Container(1).IsSelected);
        row.RemoveFromSelection(); Assert.Equal(-1, f.Combo.SelectedIndex); row.AddToSelection(); Assert.Equal(1, f.Combo.SelectedIndex);
    }

    [Theory, InlineData(0, false), InlineData(0, true), InlineData(1, false), InlineData(1, true), InlineData(2, false), InlineData(2, true)]
    public void EachRealizedRowUsesItsOwnSemanticName(int kind, bool source)
    {
        var f = new Fixture(kind, source, true); var rows = f.Rows();
        for (int i = 0; i < 3; i++) AutomationProperties.SetName(f.Container(i), "第 " + i + " 行 · 中文🙂");
        for (int i = 0; i < 3; i++) Assert.Equal("第 " + i + " 行 · 中文🙂", rows[i].GetName());
    }

    [Theory, InlineData(false), InlineData(true)]
    public void AnUnrealizedRowDoesNotBorrowARealizedEqualSiblingsName(bool source)
    {
        var f = new Fixture(1, source, false); var rows = f.Rows(); var generator = f.Combo.ItemContainerGenerator;
        using (generator.StartAt(generator.GeneratorPositionFromIndex(0), GeneratorDirection.Forward, true))
        {
            var first = Assert.IsAssignableFrom<ComboBoxItem>(generator.GenerateNext(out _)); generator.PrepareItemContainer(first);
            AutomationProperties.SetName(first, "仅第一行的名称");
        }
        generator.Remove(generator.GeneratorPositionFromIndex(1), 2);
        Assert.Null(generator.ContainerFromIndex(1)); Assert.NotNull(generator.ContainerFromIndex(0)); Assert.Equal(f.Second.ToString(), rows[1].GetName());
    }

    private sealed class Fixture
    {
        internal readonly ComboBox Combo = new();
        internal readonly ObservableCollection<object> Source;
        internal readonly object First, Second;
        internal readonly ComboBoxAutomationPeer Peer;
        internal Fixture(int kind, bool source, bool realized)
        {
            First = kind == 2 ? new EqualContainer(7) : new Row(7); Second = kind == 1 ? First : kind == 2 ? new EqualContainer(7) : new Row(7);
            Source = new() { First, Second, kind == 2 ? new EqualContainer(9) : new Row(9) };
            if (source) Combo.ItemsSource = Source; else foreach (var value in Source) Combo.Items.Add(value);
            Peer = Assert.IsType<ComboBoxAutomationPeer>(Combo.GetAutomationPeer()); if (realized) Generate();
        }
        private void Generate()
        {
            var generator = Combo.ItemContainerGenerator;
            using (generator.StartAt(generator.GeneratorPositionFromIndex(0), GeneratorDirection.Forward, true))
                for (int i = 0; i < Combo.Items.Count; i++) { var row = Assert.IsAssignableFrom<ComboBoxItem>(generator.GenerateNext(out _)); generator.PrepareItemContainer(row); }
        }
        internal ComboBoxItem Container(int index) => Assert.IsAssignableFrom<ComboBoxItem>(Combo.ItemContainerGenerator.ContainerFromIndex(index));
        internal SelectorItemAutomationPeer[] Rows() => Peer.GetChildren()!.Select(Assert.IsAssignableFrom<SelectorItemAutomationPeer>).ToArray();
        internal void AssertSelection(SelectorItemAutomationPeer[] rows, int index)
        {
            Assert.Equal(index, Combo.SelectedIndex);
            if (index < 0) { Assert.Null(Combo.SelectedItem); Assert.Empty(Peer.GetSelection()); }
            else { Assert.Same(Source[index], Combo.SelectedItem); Assert.Same(rows[index], Assert.Single(Peer.GetSelection())); }
            for (int i = 0; i < rows.Length; i++) Assert.Equal(i == index, rows[i].IsSelected);
        }
    }
    private sealed class Row(int key)
    {
        public override bool Equals(object? other) => other is Row row && key == row.Key;
        private int Key => key;
        public override int GetHashCode() => key;
        public override string ToString() => "重复条目 · 中文🙂 " + key;
    }
    private sealed class EqualContainer(int key) : ComboBoxItem
    {
        private int Key => key;
        public override bool Equals(object? other) => other is EqualContainer row && Key == row.Key;
        public override int GetHashCode() => key;
    }
}
