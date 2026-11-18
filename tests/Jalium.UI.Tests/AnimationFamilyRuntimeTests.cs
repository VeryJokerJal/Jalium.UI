using System.Collections;
using System.Diagnostics;
using Jalium.UI.Animation;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class AnimationFamilyRuntimeTests
{
    public static IEnumerable<object[]> AnimationTypes => typeof(AnimationTimeline).Assembly.GetTypes()
        .Where(type => type.IsPublic && !type.IsAbstract && !type.ContainsGenericParameters &&
            typeof(AnimationTimeline).IsAssignableFrom(type))
        .OrderBy(type => type.FullName)
        .Select(type => new object[] { type });

    [Theory]
    [MemberData(nameof(AnimationTypes))]
    public void EveryConcreteAnimation_UpdatesItsTargetNotifiesAndRestoresItsBase(Type animationType)
    {
        var animation = (AnimationTimeline)Activator.CreateInstance(animationType)!;
        animation.Duration = TimeSpan.FromSeconds(1);
        var (origin, destination) = Values(animation.TargetPropertyType);
        if (animationType.Name.EndsWith("UsingPath", StringComparison.Ordinal))
        {
            var path = new PathGeometry();
            var figure = new PathFigure { StartPoint = new Point(0, 0) };
            figure.Segments.Add(new LineSegment(new Point(100, 200)));
            path.Figures.Add(figure);
            animationType.GetProperty("PathGeometry")!.SetValue(animation, path);
            destination = animation.TargetPropertyType == typeof(double) ? 100d :
                animation.TargetPropertyType == typeof(Point) ? new Point(100, 200) : new Matrix(1, 0, 0, 1, 100, 200);
        }
        else if (animationType.GetProperty("KeyFrames")?.GetValue(animation) is IList frames)
        {
            string familyName = animation.TargetPropertyType == typeof(float) ? "Single" : animation.TargetPropertyType.Name;
            Type frameType = animationType.Assembly.GetType($"Jalium.UI.Media.Animation.Discrete{familyName}KeyFrame", throwOnError: true)!;
            var frame = (IKeyFrame)Activator.CreateInstance(frameType)!;
            frame.Value = destination;
            frame.KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1));
            frames.Add(frame);
        }
        else
        {
            Assert.NotNull(animationType.GetProperty("From"));
            Assert.NotNull(animationType.GetProperty("To"));
            animationType.GetProperty("From")!.SetValue(animation, origin);
            animationType.GetProperty("To")!.SetValue(animation, destination);
        }

        var target = new Probe(animation.TargetPropertyType, origin);
        int notifications = 0;
        target.Changed += (_, _) => notifications++;
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => target.BeginAnimation(target.Property, animation));
            AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            AnimationManager.ProcessFrame(start + Stopwatch.Frequency * 5 / 4);
            AssertValue(destination, target.GetValue(target.Property));
            Assert.True(notifications > 0, $"{animationType.Name} must notify the owner when its animated value changes.");
            Assert.False(target.CanFreeze);

            int beforeRemoval = notifications;
            target.BeginAnimation(target.Property, null);
            AssertValue(origin, target.GetValue(target.Property));
            Assert.True(notifications > beforeRemoval, "Removing an animation must notify the owner when the base value reappears.");
            Assert.True(target.CanFreeze);
        }
        finally
        {
            target.BeginAnimation(target.Property, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    private static (object Origin, object Destination) Values(Type type)
    {
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal) ||
            type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long))
            return (Convert.ChangeType(0, type), Convert.ChangeType(100, type));
        if (type == typeof(bool)) return (false, true);
        if (type == typeof(char)) return ('a', 'z');
        if (type == typeof(string) || type == typeof(object)) return ("start", "end");
        if (type == typeof(Color)) return (Colors.Black, Colors.White);
        if (type == typeof(Brush)) return (Brushes.Black, Brushes.White);
        if (type == typeof(Point)) return (new Point(0, 0), new Point(100, 200));
        if (type == typeof(Vector)) return (new Vector(0, 0), new Vector(100, 200));
        if (type == typeof(Size)) return (new Size(0, 0), new Size(100, 200));
        if (type == typeof(Rect)) return (new Rect(0, 0, 1, 1), new Rect(100, 200, 300, 400));
        if (type == typeof(Thickness)) return (new Thickness(0), new Thickness(100));
        if (type == typeof(CornerRadius)) return (new CornerRadius(0), new CornerRadius(100));
        if (type == typeof(Matrix)) return (Matrix.Identity, new Matrix(1, 0, 0, 1, 100, 200));
        if (type == typeof(Point3D)) return (new Point3D(0, 0, 0), new Point3D(100, 200, 300));
        if (type == typeof(Vector3D)) return (new Vector3D(0, 0, 0), new Vector3D(100, 200, 300));
        if (type == typeof(Size3D)) return (new Size3D(0, 0, 0), new Size3D(100, 200, 300));
        var rotation = new Quaternion(new Vector3D(0, 0, 1), 90);
        if (type == typeof(Quaternion)) return (Quaternion.Identity, rotation);
        if (type == typeof(Rotation3D)) return (new QuaternionRotation3D(Quaternion.Identity), new QuaternionRotation3D(rotation));
        throw new InvalidOperationException($"Add runtime fixtures for animation values of type {type}.");
    }

    private static void AssertValue(object expected, object? actual)
    {
        if (expected is SolidColorBrush brush)
            Assert.Equal(brush.Color, Assert.IsType<SolidColorBrush>(actual).Color);
        else if (expected is Rotation3D rotation)
        {
            var actualRotation = Assert.IsAssignableFrom<Rotation3D>(actual);
            Assert.Equal(rotation.Value.M11, actualRotation.Value.M11, 8);
            Assert.Equal(rotation.Value.M12, actualRotation.Value.M12, 8);
            Assert.Equal(rotation.Value.M22, actualRotation.Value.M22, 8);
        }
        else if (expected is Quaternion quaternion)
        {
            var actualQuaternion = Assert.IsType<Quaternion>(actual);
            Assert.Equal(quaternion.X, actualQuaternion.X, 8);
            Assert.Equal(quaternion.Y, actualQuaternion.Y, 8);
            Assert.Equal(quaternion.Z, actualQuaternion.Z, 8);
            Assert.Equal(quaternion.W, actualQuaternion.W, 8);
        }
        else Assert.Equal(expected, actual);
    }

    private sealed class Probe : Animatable
    {
        private static readonly Dictionary<Type, DependencyProperty> Properties = new();
        internal DependencyProperty Property { get; }

        internal Probe(Type type, object baseValue)
        {
            if (!Properties.TryGetValue(type, out var property))
            {
                property = DependencyProperty.Register("RuntimeValue" + type.Name, type, typeof(Probe), new PropertyMetadata(baseValue));
                Properties.Add(type, property);
            }
            Property = property;
            SetValue(property, baseValue);
        }

        protected override Freezable CreateInstanceCore() => new Probe(Property.PropertyType, GetAnimationBaseValue(Property)!);
    }
}
