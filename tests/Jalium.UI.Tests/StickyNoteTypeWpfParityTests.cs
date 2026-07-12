using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public sealed class StickyNoteTypeWpfParityTests
{
    [Fact]
    public void ValuesMatchWpf()
    {
        Assert.Equal(0, (int)StickyNoteType.Text);
        Assert.Equal(1, (int)StickyNoteType.Ink);
        Assert.Equal(["Text", "Ink"], Enum.GetNames<StickyNoteType>());
    }

    [Fact]
    public void StickyNoteControl_ExposesTypedStateAndValidatesPenWidth()
    {
        var note = new StickyNoteControl(StickyNoteType.Ink)
        {
            IsExpanded = false,
            PenWidth = 3.5,
        };

        Assert.Equal(StickyNoteType.Ink, note.StickyNoteType);
        Assert.False(note.IsExpanded);
        Assert.Equal(3.5, note.PenWidth);
        Assert.False(note.IsActive);
        Assert.Throws<ArgumentOutOfRangeException>(() => note.PenWidth = 0);
    }
}
