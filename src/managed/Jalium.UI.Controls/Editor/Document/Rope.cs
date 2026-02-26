using System.Text;

namespace Jalium.UI.Controls.Editor;

/// <summary>
/// An immutable rope data structure for efficient string manipulation.
/// Supports O(log n) insert, delete, charAt, and substring operations.
/// Each mutation returns a new Rope, sharing unchanged nodes with the previous version.
/// </summary>
public sealed class Rope
{
    internal RopeNode Root { get; }

    /// <summary>
    /// Gets the total length of the rope.
    /// </summary>
    public int Length => Root.Length;

    /// <summary>
    /// Gets the number of line breaks (\n) in the rope.
    /// </summary>
    public int LineBreakCount => Root.LineBreakCount;

    private Rope(RopeNode root)
    {
        Root = root;
    }

    /// <summary>
    /// Gets an empty rope.
    /// </summary>
    public static Rope Empty { get; } = new(RopeNode.CreateLeaf(string.Empty));

    /// <summary>
    /// Creates a rope from a string.
    /// </summary>
    public static Rope FromString(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Empty;

        if (text.Length <= RopeNode.MaxLeafLength)
            return new Rope(RopeNode.CreateLeaf(text));

        // Split into balanced tree
        return new Rope(BuildTree(text, 0, text.Length));
    }

    private static RopeNode BuildTree(string text, int start, int length)
    {
        if (length <= RopeNode.MaxLeafLength)
            return RopeNode.CreateLeaf(text.Substring(start, length));

        int mid = length / 2;
        var left = BuildTree(text, start, mid);
        var right = BuildTree(text, start + mid, length - mid);
        return RopeNode.CreateInner(left, right);
    }

    /// <summary>
    /// Gets the character at the specified index.
    /// </summary>
    public char this[int index]
    {
        get
        {
            if (index < 0 || index >= Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return Root.CharAt(index);
        }
    }

    /// <summary>
    /// Gets a substring from the rope.
    /// </summary>
    public string ToString(int startIndex, int length)
    {
        if (startIndex < 0 || length < 0 || startIndex + length > Length)
            throw new ArgumentOutOfRangeException();
        if (length == 0) return string.Empty;
        return Root.GetText(startIndex, length);
    }

    /// <summary>
    /// Returns the full text content.
    /// </summary>
    public override string ToString()
    {
        return Root.GetText(0, Length);
    }

    /// <summary>
    /// Inserts text at the specified index. Returns a new Rope.
    /// </summary>
    public Rope Insert(int index, string text)
    {
        if (index < 0 || index > Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (string.IsNullOrEmpty(text)) return this;
        return new Rope(Root.Insert(index, text));
    }

    /// <summary>
    /// Removes a range of characters. Returns a new Rope.
    /// </summary>
    public Rope Remove(int index, int length)
    {
        if (index < 0 || length < 0 || index + length > Length)
            throw new ArgumentOutOfRangeException();
        if (length == 0) return this;
        return new Rope(Root.Remove(index, length));
    }

    /// <summary>
    /// Replaces a range with new text. Returns a new Rope.
    /// </summary>
    public Rope Replace(int index, int length, string text)
    {
        if (length == 0 && string.IsNullOrEmpty(text)) return this;
        var result = length > 0 ? Remove(index, length) : this;
        return !string.IsNullOrEmpty(text) ? result.Insert(index, text) : result;
    }

    /// <summary>
    /// Writes a range of the rope to a TextWriter.
    /// </summary>
    public void WriteTo(TextWriter writer, int startIndex, int length)
    {
        if (startIndex < 0 || length < 0 || startIndex + length > Length)
            throw new ArgumentOutOfRangeException();
        Root.WriteTo(writer, startIndex, length);
    }

    /// <summary>
    /// Finds the first occurrence of a character.
    /// </summary>
    public int IndexOf(char c, int startIndex, int count)
    {
        if (startIndex < 0 || count < 0 || startIndex + count > Length)
            throw new ArgumentOutOfRangeException();

        // For simplicity, extract the range and search
        // A more efficient implementation would traverse the tree
        var text = ToString(startIndex, count);
        int idx = text.IndexOf(c);
        return idx >= 0 ? startIndex + idx : -1;
    }

    /// <summary>
    /// Finds the first occurrence of a string.
    /// </summary>
    public int IndexOf(string searchText, int startIndex, int count, StringComparison comparison = StringComparison.Ordinal)
    {
        if (startIndex < 0 || count < 0 || startIndex + count > Length)
            throw new ArgumentOutOfRangeException();

        var text = ToString(startIndex, count);
        int idx = text.IndexOf(searchText, comparison);
        return idx >= 0 ? startIndex + idx : -1;
    }
}
