namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Provides storage for dependency-property values associated with virtualized items.
/// </summary>
public interface IContainItemStorage
{
    void StoreItemValue(object item, DependencyProperty dp, object value);

    object? ReadItemValue(object item, DependencyProperty dp);

    void ClearItemValue(object item, DependencyProperty dp);

    void ClearValue(DependencyProperty dp);

    void Clear();
}

/// <summary>
/// Reports layout state used when an item participates in hierarchical virtualization.
/// </summary>
public interface IHierarchicalVirtualizationAndScrollInfo
{
    HierarchicalVirtualizationConstraints Constraints { get; set; }

    HierarchicalVirtualizationHeaderDesiredSizes HeaderDesiredSizes { get; }

    HierarchicalVirtualizationItemDesiredSizes ItemDesiredSizes { get; set; }

    Panel? ItemsHost { get; }

    bool MustDisableVirtualization { get; set; }

    bool InBackgroundLayout { get; set; }
}
