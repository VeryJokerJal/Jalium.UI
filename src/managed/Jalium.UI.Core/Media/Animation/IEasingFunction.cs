namespace Jalium.UI.Media.Animation;

/// <summary>Transforms normalized animation progress using a custom easing curve.</summary>
public interface IEasingFunction
{
    double Ease(double normalizedTime);
}
