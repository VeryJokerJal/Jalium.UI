namespace Jalium.UI.Automation;

/// <summary>Specifies the toggle state of a control.</summary>
public enum ToggleState
{
    Off = 0,
    On = 1,
    Indeterminate = 2,
}

/// <summary>Specifies the expand/collapse state of a control.</summary>
public enum ExpandCollapseState
{
    Collapsed = 0,
    Expanded = 1,
    PartiallyExpanded = 2,
    LeafNode = 3,
}

/// <summary>Specifies whether a table is primarily organized by rows or columns.</summary>
public enum RowOrColumnMajor
{
    RowMajor = 0,
    ColumnMajor = 1,
    Indeterminate = 2,
}
