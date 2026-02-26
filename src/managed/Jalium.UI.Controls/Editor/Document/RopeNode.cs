namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Internal node of an AVL-balanced rope tree.
/// Leaf nodes contain string chunks; inner nodes contain left/right children.
/// </summary>
internal sealed class RopeNode
{
    internal const int MaxLeafLength = 512;
    internal const int MinLeafLength = 128;

    // Leaf data (null for inner nodes)
    internal string? Text;

    // Inner node children (null for leaf nodes)
    internal RopeNode? Left;
    internal RopeNode? Right;

    // Cached metrics
    internal int Length;
    internal int LineBreakCount;
    internal int Height;

    private RopeNode() { }

    /// <summary>
    /// Creates a leaf node containing the specified text.
    /// </summary>
    internal static RopeNode CreateLeaf(string text)
    {
        return new RopeNode
        {
            Text = text,
            Length = text.Length,
            LineBreakCount = CountLineBreaks(text),
            Height = 1
        };
    }

    /// <summary>
    /// Creates an inner node combining two children.
    /// </summary>
    internal static RopeNode CreateInner(RopeNode left, RopeNode right)
    {
        return new RopeNode
        {
            Left = left,
            Right = right,
            Length = left.Length + right.Length,
            LineBreakCount = left.LineBreakCount + right.LineBreakCount,
            Height = 1 + Math.Max(left.Height, right.Height)
        };
    }

    internal bool IsLeaf => Text != null;

    /// <summary>
    /// Gets the balance factor (left height - right height).
    /// </summary>
    internal int BalanceFactor => (Left?.Height ?? 0) - (Right?.Height ?? 0);

    /// <summary>
    /// Rebalances the tree using AVL rotations.
    /// Returns the new root of the subtree.
    /// </summary>
    internal static RopeNode Rebalance(RopeNode node)
    {
        if (node.IsLeaf) return node;

        int bf = node.BalanceFactor;

        if (bf > 1) // Left heavy
        {
            if (node.Left != null && node.Left.BalanceFactor < 0)
            {
                // Left-Right case
                node = new RopeNode
                {
                    Left = RotateLeft(node.Left),
                    Right = node.Right,
                    Text = null
                };
                UpdateMetrics(node);
            }
            return RotateRight(node);
        }

        if (bf < -1) // Right heavy
        {
            if (node.Right != null && node.Right.BalanceFactor > 0)
            {
                // Right-Left case
                node = new RopeNode
                {
                    Left = node.Left,
                    Right = RotateRight(node.Right),
                    Text = null
                };
                UpdateMetrics(node);
            }
            return RotateLeft(node);
        }

        return node;
    }

    private static RopeNode RotateRight(RopeNode node)
    {
        var newRoot = node.Left!;
        var temp = newRoot.Right;
        var result = CreateInner(
            newRoot.Left!,
            CreateInner(temp ?? CreateLeaf(""), node.Right ?? CreateLeaf("")));
        // Correct: newRoot becomes root, its right subtree becomes node's left
        result = new RopeNode
        {
            Left = newRoot.Left,
            Right = new RopeNode
            {
                Left = newRoot.Right,
                Right = node.Right,
                Text = null
            },
            Text = null
        };
        UpdateMetrics(result.Right!);
        UpdateMetrics(result);
        return result;
    }

    private static RopeNode RotateLeft(RopeNode node)
    {
        var newRoot = node.Right!;
        var result = new RopeNode
        {
            Left = new RopeNode
            {
                Left = node.Left,
                Right = newRoot.Left,
                Text = null
            },
            Right = newRoot.Right,
            Text = null
        };
        UpdateMetrics(result.Left!);
        UpdateMetrics(result);
        return result;
    }

    internal static void UpdateMetrics(RopeNode node)
    {
        if (node.IsLeaf)
        {
            node.Length = node.Text!.Length;
            node.LineBreakCount = CountLineBreaks(node.Text);
            node.Height = 1;
        }
        else
        {
            node.Length = (node.Left?.Length ?? 0) + (node.Right?.Length ?? 0);
            node.LineBreakCount = (node.Left?.LineBreakCount ?? 0) + (node.Right?.LineBreakCount ?? 0);
            node.Height = 1 + Math.Max(node.Left?.Height ?? 0, node.Right?.Height ?? 0);
        }
    }

    /// <summary>
    /// Gets the character at the specified index.
    /// </summary>
    internal char CharAt(int index)
    {
        if (IsLeaf)
            return Text![index];

        int leftLen = Left?.Length ?? 0;
        if (index < leftLen)
            return Left!.CharAt(index);
        else
            return Right!.CharAt(index - leftLen);
    }

    /// <summary>
    /// Gets a substring from this node's subtree.
    /// </summary>
    internal string GetText(int startIndex, int length)
    {
        if (length == 0) return string.Empty;

        if (IsLeaf)
            return Text!.Substring(startIndex, length);

        int leftLen = Left?.Length ?? 0;

        if (startIndex + length <= leftLen)
            return Left!.GetText(startIndex, length);

        if (startIndex >= leftLen)
            return Right!.GetText(startIndex - leftLen, length);

        // Spans both children
        var leftPart = Left!.GetText(startIndex, leftLen - startIndex);
        var rightPart = Right!.GetText(0, length - (leftLen - startIndex));
        return leftPart + rightPart;
    }

    /// <summary>
    /// Inserts text at the specified index. Returns the new root.
    /// </summary>
    internal RopeNode Insert(int index, string text)
    {
        if (IsLeaf)
        {
            var newText = Text!.Insert(index, text);
            if (newText.Length <= MaxLeafLength)
                return CreateLeaf(newText);

            // Split into two leaves
            int mid = newText.Length / 2;
            return Rebalance(CreateInner(
                CreateLeaf(newText[..mid]),
                CreateLeaf(newText[mid..])));
        }

        int leftLen = Left?.Length ?? 0;
        RopeNode newNode;

        if (index <= leftLen)
        {
            newNode = new RopeNode
            {
                Left = (Left ?? CreateLeaf("")).Insert(index, text),
                Right = Right,
                Text = null
            };
        }
        else
        {
            newNode = new RopeNode
            {
                Left = Left,
                Right = (Right ?? CreateLeaf("")).Insert(index - leftLen, text),
                Text = null
            };
        }

        UpdateMetrics(newNode);
        return Rebalance(newNode);
    }

    /// <summary>
    /// Removes text at the specified range. Returns the new root.
    /// </summary>
    internal RopeNode Remove(int startIndex, int length)
    {
        if (length == 0) return this;

        if (IsLeaf)
        {
            var newText = Text!.Remove(startIndex, length);
            return CreateLeaf(newText);
        }

        int leftLen = Left?.Length ?? 0;

        if (startIndex + length <= leftLen)
        {
            // Entirely in left subtree
            var newLeft = Left!.Remove(startIndex, length);
            if (newLeft.Length == 0) return Right ?? CreateLeaf("");
            var newNode = new RopeNode { Left = newLeft, Right = Right, Text = null };
            UpdateMetrics(newNode);
            return Rebalance(newNode);
        }

        if (startIndex >= leftLen)
        {
            // Entirely in right subtree
            var newRight = Right!.Remove(startIndex - leftLen, length);
            if (newRight.Length == 0) return Left ?? CreateLeaf("");
            var newNode = new RopeNode { Left = Left, Right = newRight, Text = null };
            UpdateMetrics(newNode);
            return Rebalance(newNode);
        }

        // Spans both children
        int leftRemove = leftLen - startIndex;
        int rightRemove = length - leftRemove;

        var resultLeft = Left!.Remove(startIndex, leftRemove);
        var resultRight = Right!.Remove(0, rightRemove);

        if (resultLeft.Length == 0) return resultRight;
        if (resultRight.Length == 0) return resultLeft;

        var result = new RopeNode { Left = resultLeft, Right = resultRight, Text = null };
        UpdateMetrics(result);
        return Rebalance(result);
    }

    /// <summary>
    /// Writes the content to a TextWriter.
    /// </summary>
    internal void WriteTo(TextWriter writer, int startIndex, int length)
    {
        if (length == 0) return;

        if (IsLeaf)
        {
            writer.Write(Text!.AsSpan(startIndex, length));
            return;
        }

        int leftLen = Left?.Length ?? 0;

        if (startIndex + length <= leftLen)
        {
            Left!.WriteTo(writer, startIndex, length);
        }
        else if (startIndex >= leftLen)
        {
            Right!.WriteTo(writer, startIndex - leftLen, length);
        }
        else
        {
            int leftPart = leftLen - startIndex;
            Left!.WriteTo(writer, startIndex, leftPart);
            Right!.WriteTo(writer, 0, length - leftPart);
        }
    }

    private static int CountLineBreaks(string text)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') count++;
        }
        return count;
    }
}
