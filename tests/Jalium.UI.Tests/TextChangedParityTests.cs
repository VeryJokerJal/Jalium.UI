using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class TextChangedParityTests
{
    [Fact]
    public void RoutedEvents_AreOwnedByTextBoxBase()
    {
        Assert.Equal(typeof(TextBoxBase), TextBoxBase.TextChangedEvent.OwnerType);
        Assert.Equal(typeof(TextBoxBase), TextBoxBase.SelectionChangedEvent.OwnerType);
        Assert.Same(TextBoxBase.TextChangedEvent, TextBox.TextChangedEvent);
        Assert.Same(TextBoxBase.SelectionChangedEvent, TextBox.SelectionChangedEvent);
    }

    [Fact]
    public void Constructor_StoresActionAndChanges()
    {
        var change = new TextChange { Offset = 2, AddedLength = 3, RemovedLength = 1 };
        ICollection<TextChange> changes = new[] { change };

        var args = new TextChangedEventArgs(
            TextBoxBase.TextChangedEvent,
            UndoAction.Merge,
            changes);

        Assert.Same(TextBoxBase.TextChangedEvent, args.RoutedEvent);
        Assert.Equal(UndoAction.Merge, args.UndoAction);
        Assert.Same(changes, args.Changes);
        Assert.Equal(2, change.Offset);
        Assert.Equal(3, change.AddedLength);
        Assert.Equal(1, change.RemovedLength);
    }

    [Fact]
    public void ShortConstructor_UsesEmptyChangesCollection()
    {
        var args = new TextChangedEventArgs(TextBoxBase.TextChangedEvent, UndoAction.None);

        Assert.Empty(args.Changes);
    }

    [Fact]
    public void Constructor_RejectsNullEventAndInvalidAction()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TextChangedEventArgs(null!, UndoAction.None));
        Assert.Throws<InvalidEnumArgumentException>(() =>
            new TextChangedEventArgs(TextBoxBase.TextChangedEvent, (UndoAction)99));
    }

    [Fact]
    public void TextAssignment_RaisesTypedEventWithChangedRange()
    {
        var textBox = new TextBox { Text = "abcXYZ" };
        TextChangedEventArgs? observed = null;
        textBox.TextChanged += (_, e) => observed = e;

        textBox.Text = "abc123XYZ";

        Assert.NotNull(observed);
        TextChange change = Assert.Single(observed.Changes);
        Assert.Equal(3, change.Offset);
        Assert.Equal(3, change.AddedLength);
        Assert.Equal(0, change.RemovedLength);
        Assert.Same(textBox, observed.Source);
    }

    [Fact]
    public void SelectionChange_RaisesInheritedEvent()
    {
        var textBox = new TextBox { Text = "abc" };
        int raised = 0;
        textBox.SelectionChanged += (_, _) => raised++;

        textBox.Select(0, 1);

        Assert.Equal(1, raised);
    }
}
