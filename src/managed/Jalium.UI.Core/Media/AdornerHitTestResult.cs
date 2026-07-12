using Jalium.UI.Documents;

namespace Jalium.UI.Media;

/// <summary>Describes a point hit on an adorner and the visual within it that was hit.</summary>
public sealed class AdornerHitTestResult : PointHitTestResult
{
    internal AdornerHitTestResult(Visual visual, Point point, Adorner adorner)
        : base(visual, point)
    {
        Adorner = adorner ?? throw new ArgumentNullException(nameof(adorner));
    }

    /// <summary>Gets the adorner that owns the hit visual.</summary>
    public Adorner Adorner { get; }
}
