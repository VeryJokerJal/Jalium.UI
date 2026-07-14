using System.Reflection;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// Guards canonical namespace ownership so historical compatibility types cannot
/// quietly become public again.
/// </summary>
public sealed class CanonicalTypeConflictTests
{
    [Fact]
    public void ControlsTypes_AreNotPublishedFromThePrimitivesNamespace()
    {
        Type[] exported = typeof(FrameworkElement).Assembly.GetExportedTypes();
        string[] names =
        [
            nameof(Controls.ClickMode),
            nameof(Controls.Decorator),
            nameof(Controls.ItemsPresenter),
            nameof(Controls.ScrollContentPresenter),
            nameof(Controls.SelectionMode),
            nameof(Controls.SelectiveScrollingOrientation),
        ];

        Assert.All(names, name =>
        {
            Assert.Contains(exported, type => type.FullName == $"Jalium.UI.Controls.{name}");
            Assert.DoesNotContain(exported, type => type.FullName == $"Jalium.UI.Controls.Primitives.{name}");
        });
        Assert.DoesNotContain(exported, type => type.FullName == "Jalium.UI.Documents.Decorator");

        Assert.True(typeof(Controls.ScrollContentPresenter).IsSealed);
        Assert.Equal(typeof(Controls.Decorator), typeof(Controls.Border).BaseType);
        Assert.Equal(
            typeof(Controls.ClickMode),
            typeof(Controls.Primitives.ButtonBase).GetProperty(nameof(Controls.Primitives.ButtonBase.ClickMode))!.PropertyType);
        Assert.Equal(
            typeof(Controls.SelectionMode),
            typeof(Controls.ListBox).GetProperty(nameof(Controls.ListBox.SelectionMode))!.PropertyType);
    }

    [Fact]
    public void BlurEffect_NameIsReservedForTheCanonicalElementEffect()
    {
        Type[] exported = typeof(FrameworkElement).Assembly.GetExportedTypes();

        Assert.DoesNotContain(exported, type => type.FullName == "Jalium.UI.Media.BlurEffect");
        Assert.Contains(exported, type => type.FullName == "Jalium.UI.Media.Effects.BlurEffect");
        Assert.Contains(exported, type => type.FullName == "Jalium.UI.Media.BackdropBlurEffect");

        var backdrop = new Media.BackdropBlurEffect(12f, BackdropBlurType.Gaussian);
        Assert.Equal(12f, backdrop.BlurRadius);
        Assert.Equal(4f, backdrop.BlurSigma);
        Assert.True(backdrop.HasEffect);
    }

    [Fact]
    public void RenderCacheImplementation_DoesNotPolluteThePublicWpfSurface()
    {
        Type[] exported = typeof(FrameworkElement).Assembly.GetExportedTypes();
        string[] implementationTypes =
        [
            "Jalium.UI.Media.Rendering.Drawing",
            "Jalium.UI.Media.Rendering.RecordedDrawing",
            "Jalium.UI.Media.Rendering.MediaRenderCacheHost",
            "Jalium.UI.Rendering.ICacheableDrawingContext",
            "Jalium.UI.Rendering.IRenderCacheHost",
        ];

        Assert.All(
            implementationTypes,
            name => Assert.DoesNotContain(exported, type => type.FullName == name));
        Assert.Contains(exported, type => type.FullName == "Jalium.UI.Media.Drawing");
    }

    [Fact]
    public void MessageBoxAndSystemCommands_AreExportedOnlyFromRootNamespace()
    {
        Assembly implementation = typeof(FrameworkElement).Assembly;
        Type[] exported = implementation.GetExportedTypes();

        string[] canonicalNames =
        [
            "Jalium.UI.MessageBox",
            "Jalium.UI.MessageBoxButton",
            "Jalium.UI.MessageBoxImage",
            "Jalium.UI.MessageBoxOptions",
            "Jalium.UI.MessageBoxResult",
            "Jalium.UI.SplashScreen",
            "Jalium.UI.ShutdownMode",
            "Jalium.UI.SystemCommands",
            "Jalium.UI.Window",
            "Jalium.UI.ResizeMode",
            "Jalium.UI.WindowStartupLocation",
            "Jalium.UI.WindowState",
            "Jalium.UI.WindowStyle",
        ];
        Assert.All(
            canonicalNames,
            name => Assert.Contains(exported, type => type.FullName == name));

        Assert.DoesNotContain(exported, type =>
            type.Namespace == "Jalium.UI.Controls" &&
            canonicalNames.Any(name => name.EndsWith('.' + type.Name, StringComparison.Ordinal)));

        Assert.Equal(
            ["OK", "OKCancel", "YesNoCancel", "YesNo"],
            Enum.GetNames<MessageBoxButton>());
        Assert.Equal(
            ["None", "OK", "Cancel", "Yes", "No"],
            Enum.GetNames<MessageBoxResult>());

        Assert.Empty(typeof(SplashScreen).GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        Assert.Equal(typeof(WindowState), typeof(Window).GetProperty(nameof(Window.WindowState))!.PropertyType);
        Assert.Equal(typeof(ResizeMode), typeof(Window).GetProperty(nameof(Window.ResizeMode))!.PropertyType);
        Assert.Equal(typeof(WindowStyle), typeof(Window).GetProperty(nameof(Window.WindowStyle))!.PropertyType);
        Assert.Equal(
            typeof(WindowStartupLocation),
            typeof(Window).GetProperty(nameof(Window.WindowStartupLocation))!.PropertyType);
        Assert.Equal(
            typeof(ShutdownMode),
            typeof(Application).GetProperty(nameof(Application.ShutdownMode))!.PropertyType);
        Assert.Equal(
            typeof(Window),
            typeof(SystemCommands).GetMethod(nameof(SystemCommands.CloseWindow))!.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void ControlTemplate_IsExportedOnlyFromControlsNamespace()
    {
        Assembly implementation = typeof(FrameworkElement).Assembly;
        Type[] exported = implementation.GetExportedTypes();

        Type canonical = Assert.Single(
            exported,
            type => type.FullName == "Jalium.UI.Controls.ControlTemplate");
        Assert.Equal(typeof(FrameworkTemplate), canonical.BaseType);

        string[] retiredNames =
        [
            "Jalium.UI.ControlTemplate",
            "Jalium.UI.ContentPresenterPlaceholder",
            "Jalium.UI.ITemplatedControl",
            "Jalium.UI.TemplateRoot",
        ];

        Assert.All(
            retiredNames,
            name => Assert.DoesNotContain(exported, type => type.FullName == name));
    }

    [Fact]
    public void CompositionTarget_IsExportedOnlyFromMediaNamespace()
    {
        Assembly implementation = typeof(FrameworkElement).Assembly;
        Type[] exported = implementation.GetExportedTypes();

        Assert.DoesNotContain(exported, type => type.FullName == "Jalium.UI.CompositionTarget");
        Type canonical = Assert.Single(
            exported,
            type => type.FullName == "Jalium.UI.Media.CompositionTarget");
        Assert.True(canonical.IsAbstract);
        Assert.False(canonical.IsSealed);
        Assert.NotNull(canonical.GetEvent(
            "Rendering",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));

        string[] implementationOnlyMembers =
        [
            "FrameInterval",
            "FrameIntervalMs",
            "RefreshRate",
            "TargetFrameRate",
            "IsActive",
            "Subscribe",
            "Unsubscribe",
            "RequestFrame",
        ];
        Assert.All(
            implementationOnlyMembers,
            name => Assert.DoesNotContain(
                canonical.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly),
                member => member.Name == name));
    }

    [Fact]
    public void GeneralTransform_IsExportedOnlyFromMediaNamespace()
    {
        Assembly implementation = typeof(FrameworkElement).Assembly;
        Type[] exported = implementation.GetExportedTypes();

        Assert.Contains(exported, type => type.FullName == "Jalium.UI.Media.GeneralTransform");
        Assert.Contains(exported, type => type.FullName == "Jalium.UI.Media.GeneralTransformGroup");

        string[] retiredNames =
        [
            "Jalium.UI.GeneralTransform",
            "Jalium.UI.GeneralTransformGroup",
            "Jalium.UI.TranslateTransform2D",
            "Jalium.UI.MatrixGeneralTransform",
            "Jalium.UI.Documents.GeneralTransform",
            "Jalium.UI.Documents.GeneralTransformGroup",
            "Jalium.UI.Documents.TranslateTransform2D",
        ];

        Assert.All(
            retiredNames,
            name => Assert.DoesNotContain(exported, type => type.FullName == name));

        MethodInfo transformToVisual = typeof(Visual).GetMethod(
            nameof(Visual.TransformToVisual),
            BindingFlags.Instance | BindingFlags.Public,
            null,
            [typeof(Visual)],
            null)!;
        Assert.Equal(typeof(Media.GeneralTransform), transformToVisual.ReturnType);
    }

    [Fact]
    public void VisualFamily_IsExportedOnlyFromMediaNamespaceWithCanonicalBaseChain()
    {
        Assembly implementation = typeof(FrameworkElement).Assembly;
        Type[] exported = implementation.GetExportedTypes();
        string[] names =
        [
            nameof(Visual),
            nameof(VisualTreeHelper),
            nameof(ContainerVisual),
            nameof(DrawingVisual),
            nameof(VisualCollection),
        ];

        Assert.All(names, name =>
        {
            Assert.Contains(exported, type => type.FullName == $"Jalium.UI.Media.{name}");
            Assert.DoesNotContain(exported, type => type.FullName == $"Jalium.UI.{name}");
        });

        Assert.True(typeof(Visual).IsAbstract);
        Assert.Equal(typeof(DependencyObject), typeof(Visual).BaseType);
        Assert.Equal(typeof(Visual), typeof(ContainerVisual).BaseType);
        Assert.Equal(typeof(ContainerVisual), typeof(DrawingVisual).BaseType);
        Assert.True(typeof(VisualCollection).IsSealed);
        Assert.True(typeof(VisualTreeHelper).IsAbstract);
        Assert.True(typeof(VisualTreeHelper).IsSealed);
        Assert.Same(typeof(Visual), Markup.XamlTypeRegistry.GetType(nameof(Visual)));
    }
}
