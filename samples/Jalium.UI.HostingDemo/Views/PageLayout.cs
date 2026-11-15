using Jalium.UI.Controls;
using Jalium.UI.HostingDemo.Themes;

namespace Jalium.UI.HostingDemo.Views;

/// <summary>Base contract shared by all DI-created demonstration pages.</summary>
public class HostingDemoPage : Page
{
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(nameof(Heading), typeof(string), typeof(HostingDemoPage), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(HostingDemoPage), new PropertyMetadata(""));
    public string Heading { get => (string)GetValue(HeadingProperty)!; set => SetValue(HeadingProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty)!; set => SetValue(DescriptionProperty, value); }
    public HostingDemoPage() { HostingDemoTheme.Register(); DefaultStyleKey = GetType(); }
}

/// <summary>Reusable heading, description and content presentation with scrolling.</summary>
public class HostingPageLayout : ContentControl
{
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(nameof(Heading), typeof(string), typeof(HostingPageLayout), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(HostingPageLayout), new PropertyMetadata(""));
    public string Heading { get => (string)GetValue(HeadingProperty)!; set => SetValue(HeadingProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty)!; set => SetValue(DescriptionProperty, value); }
    public HostingPageLayout() { HostingDemoTheme.Register(); DefaultStyleKey = typeof(HostingPageLayout); }
}
