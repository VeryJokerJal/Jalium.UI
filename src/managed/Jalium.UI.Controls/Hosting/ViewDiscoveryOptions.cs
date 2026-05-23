using System.ComponentModel;
using System.Reflection;
using Jalium.Extensions.DependencyInjection;

namespace Jalium.UI.Hosting;

/// <summary>
/// Configuration for <see cref="JaliumHostingExtensions.AddViewsAndViewModels(IServiceCollection, Action{ViewDiscoveryOptions}?)"/>
/// and friends: which assemblies to scan, how to recognise a View / ViewModel
/// by name convention, and which lifetime to register with.
/// </summary>
public sealed class ViewDiscoveryOptions
{
    /// <summary>
    /// Assemblies to scan for views and view-models. When empty, the
    /// convenience overloads default to the assembly that called
    /// <c>AddViewsAndViewModels</c>.
    /// </summary>
    public List<Assembly> Assemblies { get; } = new();

    /// <summary>
    /// Suffixes used to recognise a class as a View. Any match qualifies,
    /// case-sensitive. Defaults mirror WPF/UWP conventions:
    /// <c>Page</c>, <c>Window</c>, <c>UserControl</c>, <c>View</c>.
    /// </summary>
    public List<string> ViewSuffixes { get; } = new() { "Page", "Window", "UserControl", "View" };

    /// <summary>
    /// Suffix used to recognise a class as a ViewModel. Default: <c>ViewModel</c>.
    /// </summary>
    public string ViewModelSuffix { get; set; } = "ViewModel";

    /// <summary>
    /// Service lifetime applied to discovered View types. Default
    /// <see cref="ServiceLifetime.Transient"/> — each navigation gets a fresh
    /// Page instance.
    /// </summary>
    public ServiceLifetime ViewLifetime { get; set; } = ServiceLifetime.Transient;

    /// <summary>
    /// Service lifetime applied to discovered ViewModel types. Default
    /// <see cref="ServiceLifetime.Transient"/>; switch to
    /// <see cref="ServiceLifetime.Singleton"/> when you want a VM to retain
    /// state across navigations.
    /// </summary>
    public ServiceLifetime ViewModelLifetime { get; set; } = ServiceLifetime.Transient;

    /// <summary>
    /// Optional filter invoked for every candidate View type. Returning
    /// <see langword="false"/> skips registration for that type. Use to
    /// exclude test fixtures, design-time stubs, abstract bases, etc.
    /// </summary>
    public Func<Type, bool>? ViewFilter { get; set; }

    /// <summary>
    /// Optional filter invoked for every candidate ViewModel type.
    /// </summary>
    public Func<Type, bool>? ViewModelFilter { get; set; }

    /// <summary>
    /// When <see langword="true"/> (default), a discovered View whose matching
    /// ViewModel (by name convention) exists will be paired via
    /// <see cref="ViewRegistry.Register"/>, so <see cref="IViewFactory"/>
    /// auto-assigns the VM as <see cref="FrameworkElement.DataContext"/>.
    /// Set to <see langword="false"/> to register Views and ViewModels
    /// separately without creating automatic pairings.
    /// </summary>
    public bool AutoPair { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/> (default), a class whose name ends with
    /// <see cref="ViewModelSuffix"/> only qualifies as a ViewModel if it
    /// implements at least one of <see cref="ViewModelNotifyInterfaces"/>.
    /// This filters out plain DTOs / records that happen to end in
    /// "ViewModel" but can't drive the UI.
    /// Set to <see langword="false"/> to accept any type matching the suffix.
    /// </summary>
    public bool RequireNotifyingViewModel { get; set; } = true;

    /// <summary>
    /// Interfaces that mark a class as a "notifying" ViewModel. A type
    /// implementing any one of these passes the
    /// <see cref="RequireNotifyingViewModel"/> filter.
    /// Defaults: <see cref="INotifyPropertyChanged"/>,
    /// <see cref="INotifyPropertyChanging"/>. Add your own (e.g. a custom
    /// <c>IRaisePropertyChanged</c>) to extend the rule.
    /// </summary>
    public List<Type> ViewModelNotifyInterfaces { get; } = new()
    {
        typeof(INotifyPropertyChanged),
        typeof(INotifyPropertyChanging),
    };
}
