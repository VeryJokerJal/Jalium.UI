using Jalium.UI.Controls;
using Jalium.Extensions.DependencyInjection;

namespace Jalium.UI.Hosting;

/// <summary>
/// Attached property host that lets any <see cref="FrameworkElement"/> opt into
/// automatic ViewModel resolution — useful when a view is constructed directly
/// (XAML / <c>new MyPage()</c>) without going through <see cref="IViewFactory"/>.
/// </summary>
/// <remarks>
/// Usage (code):
/// <code>
/// var page = new MyPage();
/// ViewModelLocator.SetAutoWireViewModel(page, true);
/// </code>
/// Usage (JALXAML):
/// <code>
/// &lt;UserControl xmlns:vm="clr-namespace:Jalium.UI.Hosting"
///              vm:ViewModelLocator.AutoWireViewModel="True"&gt;
/// </code>
/// The property handler looks up <see cref="IViewFactory"/> from
/// <see cref="Application.Services"/> and, if a pairing was registered via
/// <c>AddView&lt;TView, TViewModel&gt;()</c>, resolves and assigns the ViewModel
/// to <see cref="FrameworkElement.DataContext"/>.
/// </remarks>
public static class ViewModelLocator
{
    /// <summary>
    /// Identifies the <c>AutoWireViewModel</c> attached property.
    /// </summary>
    public static readonly DependencyProperty AutoWireViewModelProperty =
        DependencyProperty.RegisterAttached(
            "AutoWireViewModel",
            typeof(bool),
            typeof(ViewModelLocator),
            new PropertyMetadata(false, OnAutoWireViewModelChanged));

    /// <summary>
    /// Gets the value of the <c>AutoWireViewModel</c> attached property.
    /// </summary>
    public static bool GetAutoWireViewModel(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(AutoWireViewModelProperty) ?? false);
    }

    /// <summary>
    /// Sets the value of the <c>AutoWireViewModel</c> attached property.
    /// Setting to <see langword="true"/> triggers an immediate DataContext
    /// resolution attempt.
    /// </summary>
    public static void SetAutoWireViewModel(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AutoWireViewModelProperty, value);
    }

    private static void OnAutoWireViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
        {
            return;
        }

        if (d is not FrameworkElement view)
        {
            return;
        }

        TryWire(view);
    }

    /// <summary>
    /// Resolves and attaches a ViewModel to <paramref name="view"/> using the
    /// currently attached <see cref="IViewFactory"/>. Safe to call before or
    /// after the view enters the visual tree.
    /// </summary>
    /// <returns><see langword="true"/> if a ViewModel was assigned.</returns>
    public static bool TryWire(FrameworkElement view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var services = Application.Current?.Services;
        if (services == null)
        {
            return false;
        }

        var factory = services.GetService<IViewFactory>();
        return factory != null && factory.TryAttachViewModel(view);
    }
}
