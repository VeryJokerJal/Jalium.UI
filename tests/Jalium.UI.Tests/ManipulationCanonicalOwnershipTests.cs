using System.Reflection;
using Jalium.UI.Ink;
using Jalium.UI.Input;
using Jalium.UI.Input.Manipulations;

namespace Jalium.UI.Tests;

public sealed class ManipulationCanonicalOwnershipTests
{
    [Fact]
    public void ManipulationContracts_HaveOneCanonicalPublicOwner()
    {
        Assembly assembly = typeof(ManipulationDelta).Assembly;
        Type[] exported = assembly.GetExportedTypes();
        string[] canonicalNames =
        [
            nameof(ManipulationDelta),
            nameof(ManipulationVelocities),
            nameof(ManipulationStartedEventArgs),
            nameof(ManipulationDeltaEventArgs),
            nameof(ManipulationCompletedEventArgs),
            nameof(ManipulationInertiaStartingEventArgs),
            nameof(InertiaTranslationBehavior),
            nameof(InertiaRotationBehavior),
            nameof(InertiaExpansionBehavior),
        ];

        foreach (string name in canonicalNames)
        {
            Type type = Assert.Single(exported, candidate => candidate.Name == name);
            Assert.Equal("Jalium.UI.Input", type.Namespace);
        }

        Assert.DoesNotContain(exported, type =>
            type.Namespace is not null &&
            type.Namespace.StartsWith("Jalium.UI.Input.Gestures", StringComparison.Ordinal));
        Assert.Null(assembly.GetType("Jalium.UI.Input.Gestures.GestureRecognizer"));

        Type? internalEngine = assembly.GetType("Jalium.UI.Input.Internal.Gestures.GestureRecognizer");
        Assert.NotNull(internalEngine);
        Assert.False(internalEngine!.IsPublic);
    }

    [Fact]
    public void ValueAndEventContracts_MatchWpfPublicShape()
    {
        Assert.False(typeof(ManipulationDelta).IsSealed);
        Assert.False(typeof(ManipulationVelocities).IsSealed);
        Assert.False(typeof(ManipulationPivot).IsSealed);

        Assert.Null(typeof(ManipulationDelta).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(ManipulationDelta).GetConstructor(
            [typeof(Vector), typeof(double), typeof(Vector), typeof(Vector)]));
        Assert.Null(typeof(ManipulationVelocities).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(ManipulationVelocities).GetConstructor(
            [typeof(Vector), typeof(double), typeof(Vector)]));

        AssertReadOnlyPublicProperties(
            typeof(ManipulationDelta),
            nameof(ManipulationDelta.Translation),
            nameof(ManipulationDelta.Rotation),
            nameof(ManipulationDelta.Scale),
            nameof(ManipulationDelta.Expansion));
        AssertReadOnlyPublicProperties(
            typeof(ManipulationVelocities),
            nameof(ManipulationVelocities.LinearVelocity),
            nameof(ManipulationVelocities.AngularVelocity),
            nameof(ManipulationVelocities.ExpansionVelocity));

        Type[] eventArgsTypes =
        [
            typeof(ManipulationStartingEventArgs),
            typeof(ManipulationStartedEventArgs),
            typeof(ManipulationDeltaEventArgs),
            typeof(ManipulationCompletedEventArgs),
            typeof(ManipulationInertiaStartingEventArgs),
            typeof(ManipulationBoundaryFeedbackEventArgs),
        ];
        Assert.All(eventArgsTypes, type => Assert.Empty(type.GetConstructors()));

        Assert.Equal(typeof(Vector), typeof(InertiaExpansionBehavior)
            .GetProperty(nameof(InertiaExpansionBehavior.DesiredExpansion))!.PropertyType);
        Assert.Equal(typeof(double), typeof(InertiaExpansionBehavior)
            .GetProperty(nameof(InertiaExpansionBehavior.InitialRadius))!.PropertyType);
        Assert.Null(typeof(ManipulationDelta).Assembly.GetType("Jalium.UI.Input.InertiaParameters2D"));
        Assert.Equal("Jalium.UI.Input.Manipulations", typeof(InertiaParameters2D).Namespace);
    }

    [Fact]
    public void InertiaBehaviors_EnforceWpfDefaultsAndExclusiveTargets()
    {
        var translation = new InertiaTranslationBehavior();
        Assert.True(double.IsNaN(translation.InitialVelocity.X));
        Assert.True(double.IsNaN(translation.InitialVelocity.Y));
        Assert.True(double.IsNaN(translation.DesiredDisplacement));
        Assert.True(double.IsNaN(translation.DesiredDeceleration));

        translation.DesiredDisplacement = 120;
        Assert.Equal(120, translation.DesiredDisplacement);
        Assert.True(double.IsNaN(translation.DesiredDeceleration));
        translation.DesiredDeceleration = 0.01;
        Assert.Equal(0.01, translation.DesiredDeceleration);
        Assert.True(double.IsNaN(translation.DesiredDisplacement));
        Assert.Throws<ArgumentOutOfRangeException>(() => translation.DesiredDeceleration = double.NaN);

        var expansion = new InertiaExpansionBehavior();
        Assert.Equal(1.0, expansion.InitialRadius);
        expansion.DesiredExpansion = new Vector(8, 12);
        expansion.DesiredDeceleration = 0.02;
        Assert.True(double.IsNaN(expansion.DesiredExpansion.X));
        Assert.True(double.IsNaN(expansion.DesiredExpansion.Y));

        var directDelta = new ManipulationDeltaEventArgs { IsInertial = false };
        Assert.True(directDelta.Cancel());
        var inertialDelta = new ManipulationDeltaEventArgs { IsInertial = true };
        Assert.False(inertialDelta.Cancel());
        var inertialCompleted = new ManipulationCompletedEventArgs { IsInertial = true };
        Assert.False(inertialCompleted.Cancel());
    }

    [Fact]
    public void InkGestureRecognizer_IsCanonicalAndUsesManagedRecognitionCore()
    {
        Assembly assembly = typeof(GestureRecognizer).Assembly;
        Assert.Equal("Jalium.UI.Ink", typeof(GestureRecognizer).Namespace);
        Assert.Equal(typeof(DependencyObject), typeof(GestureRecognizer).BaseType);
        Assert.Contains(typeof(IDisposable), typeof(GestureRecognizer).GetInterfaces());
        Assert.Null(assembly.GetType("Jalium.UI.Input.Gestures.GestureRecognizer"));
        Assert.Empty(typeof(GestureRecognitionResult).GetConstructors());

        using var recognizer = new GestureRecognizer([ApplicationGesture.Right]);
        Assert.True(recognizer.IsRecognizerAvailable);
        Assert.Equal(ApplicationGesture.Right, Assert.Single(recognizer.GetEnabledGestures()));

        var points = new StylusPointCollection(
        [
            new StylusPoint(0, 0),
            new StylusPoint(80, 2),
        ]);
        var strokes = new StrokeCollection([new Stroke(points)]);
        GestureRecognitionResult result = Assert.Single(recognizer.Recognize(strokes));
        Assert.Equal(ApplicationGesture.Right, result.ApplicationGesture);
        Assert.Equal(RecognitionConfidence.Strong, result.RecognitionConfidence);

        recognizer.SetEnabledGestures([ApplicationGesture.Left]);
        Assert.Empty(recognizer.Recognize(strokes));
        Assert.Throws<ArgumentException>(() => recognizer.SetEnabledGestures([]));
        Assert.Throws<ArgumentException>(() => recognizer.SetEnabledGestures(
            [ApplicationGesture.AllGestures, ApplicationGesture.Right]));
    }

    private static void AssertReadOnlyPublicProperties(Type type, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            PropertyInfo property = type.GetProperty(propertyName)!;
            Assert.NotNull(property);
            Assert.True(property.GetMethod!.IsPublic);
            Assert.False(property.SetMethod?.IsPublic ?? false);
        }
    }
}
