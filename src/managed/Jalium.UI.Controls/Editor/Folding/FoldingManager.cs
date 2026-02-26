namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Manages code folding sections for the editor.
/// </summary>
public sealed class FoldingManager
{
    private readonly List<FoldingSection> _foldings = [];
    private readonly Dictionary<int, FoldingSection> _foldingByStartLine = [];
    private TextDocument? _document;
    private int _version;

    /// <summary>
    /// Gets the current folding sections.
    /// </summary>
    public IReadOnlyList<FoldingSection> Foldings => _foldings;

    /// <summary>
    /// Gets a monotonic version that changes whenever folding structure/state changes.
    /// </summary>
    public int Version => _version;

    /// <summary>
    /// Gets whether any folding section is currently collapsed.
    /// </summary>
    public bool HasFoldedSections
    {
        get
        {
            for (int i = 0; i < _foldings.Count; i++)
            {
                if (_foldings[i].IsFolded)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Gets or sets the document to manage foldings for.
    /// </summary>
    public TextDocument? Document
    {
        get => _document;
        set
        {
            _document = value;
            _foldings.Clear();
            _foldingByStartLine.Clear();
            _version++;
        }
    }

    /// <summary>
    /// Updates folding sections using the specified strategy.
    /// Preserves the folded state of existing sections that match new ones.
    /// </summary>
    public void UpdateFoldings(IFoldingStrategy strategy)
    {
        if (_document == null) return;

        var previous = new (int StartLine, int EndLine, int GuideStartLine, int GuideEndLine, int StartColumn, bool IsFolded, string Title)[_foldings.Count];
        for (int i = 0; i < _foldings.Count; i++)
        {
            var existing = _foldings[i];
            previous[i] = (existing.StartLine, existing.EndLine, existing.GuideStartLine, existing.GuideEndLine, existing.StartColumn, existing.IsFolded, existing.Title);
        }

        // Remember which ranges were folded
        var wasFolded = new HashSet<(int start, int end)>();
        foreach (var f in _foldings)
        {
            if (f.IsFolded)
                wasFolded.Add((f.StartLine, f.EndLine));
        }

        _foldings.Clear();
        _foldingByStartLine.Clear();

        foreach (var section in strategy.CreateFoldings(_document))
        {
            if (wasFolded.Contains((section.StartLine, section.EndLine)))
                section.IsFolded = true;
            _foldings.Add(section);
            if (!_foldingByStartLine.ContainsKey(section.StartLine))
                _foldingByStartLine.Add(section.StartLine, section);
        }

        bool changed = previous.Length != _foldings.Count;
        if (!changed)
        {
            for (int i = 0; i < _foldings.Count; i++)
            {
                var current = _foldings[i];
                var old = previous[i];
                if (current.StartLine != old.StartLine ||
                    current.EndLine != old.EndLine ||
                    current.GuideStartLine != old.GuideStartLine ||
                    current.GuideEndLine != old.GuideEndLine ||
                    current.StartColumn != old.StartColumn ||
                    current.IsFolded != old.IsFolded ||
                    !string.Equals(current.Title, old.Title, StringComparison.Ordinal))
                {
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
            _version++;
    }

    /// <summary>
    /// Gets the folding section that starts at the specified line, if any.
    /// </summary>
    public FoldingSection? GetFoldingAt(int lineNumber)
    {
        return _foldingByStartLine.TryGetValue(lineNumber, out var section) ? section : null;
    }

    /// <summary>
    /// Gets the innermost folded section that contains the specified line.
    /// Returns null when the line is not hidden by a collapsed fold.
    /// </summary>
    public FoldingSection? GetContainingFolding(int lineNumber)
    {
        FoldingSection? candidate = null;
        foreach (var f in _foldings)
        {
            if (!f.IsFolded)
                continue;

            if (lineNumber <= f.StartLine || lineNumber > f.EndLine)
                continue;

            if (candidate == null || f.StartLine >= candidate.StartLine)
                candidate = f;
        }

        return candidate;
    }

    /// <summary>
    /// Checks whether the specified line is hidden by a collapsed fold.
    /// </summary>
    public bool IsLineHidden(int lineNumber)
    {
        foreach (var f in _foldings)
        {
            if (f.IsFolded && lineNumber > f.StartLine && lineNumber <= f.EndLine)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Toggles the fold state at the specified line.
    /// </summary>
    public bool ToggleFold(int lineNumber)
    {
        var section = GetFoldingAt(lineNumber);
        if (section != null)
        {
            section.IsFolded = !section.IsFolded;
            _version++;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Expands all collapsed sections.
    /// </summary>
    public void ExpandAll()
    {
        bool changed = false;
        foreach (var f in _foldings)
        {
            if (!f.IsFolded)
                continue;

            f.IsFolded = false;
            changed = true;
        }

        if (changed)
            _version++;
    }

    /// <summary>
    /// Collapses all sections.
    /// </summary>
    public void CollapseAll()
    {
        bool changed = false;
        foreach (var f in _foldings)
        {
            if (f.IsFolded)
                continue;

            f.IsFolded = true;
            changed = true;
        }

        if (changed)
            _version++;
    }

    /// <summary>
    /// Clears all folding sections.
    /// </summary>
    public void Clear()
    {
        if (_foldings.Count == 0)
            return;

        _foldings.Clear();
        _foldingByStartLine.Clear();
        _version++;
    }
}
