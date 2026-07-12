namespace Jalium.UI;

/// <summary>
/// Provides the common sealing contract for style and trigger setters.
/// </summary>
public abstract class SetterBase
{
    private bool _isSealed;

    internal SetterBase()
    {
    }

    /// <summary>
    /// Gets whether this setter is immutable.
    /// </summary>
    public bool IsSealed => _isSealed;

    /// <summary>
    /// Throws when a caller attempts to change a sealed setter.
    /// </summary>
    protected void CheckSealed()
    {
        if (_isSealed)
        {
            throw new InvalidOperationException("A sealed SetterBase cannot be changed.");
        }
    }

    /// <summary>
    /// Makes this setter immutable. Derived classes validate their state before calling base.
    /// </summary>
    internal virtual void Seal()
    {
        _isSealed = true;
    }
}
