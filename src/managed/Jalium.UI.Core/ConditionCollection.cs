using System.Collections.ObjectModel;

namespace Jalium.UI;

/// <summary>
/// Represents the conditions used by multi-property and multi-data triggers.
/// </summary>
public sealed class ConditionCollection : Collection<Condition>
{
    private bool _isSealed;

    /// <summary>
    /// Gets whether this collection can no longer be modified.
    /// </summary>
    public bool IsSealed => _isSealed;

    /// <inheritdoc />
    protected override void ClearItems()
    {
        VerifyMutable();
        base.ClearItems();
    }

    /// <inheritdoc />
    protected override void InsertItem(int index, Condition item)
    {
        VerifyMutable();
        ArgumentNullException.ThrowIfNull(item);
        base.InsertItem(index, item);
    }

    /// <inheritdoc />
    protected override void RemoveItem(int index)
    {
        VerifyMutable();
        base.RemoveItem(index);
    }

    /// <inheritdoc />
    protected override void SetItem(int index, Condition item)
    {
        VerifyMutable();
        ArgumentNullException.ThrowIfNull(item);
        base.SetItem(index, item);
    }

    internal void Seal(bool dataConditions)
    {
        if (_isSealed)
        {
            return;
        }

        foreach (Condition condition in this)
        {
            condition.Seal(dataConditions);
        }

        _isSealed = true;
    }

    private void VerifyMutable()
    {
        if (_isSealed)
        {
            throw new InvalidOperationException("A sealed ConditionCollection cannot be changed.");
        }
    }
}
