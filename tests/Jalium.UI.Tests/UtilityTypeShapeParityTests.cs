using System.Reflection;
using JaliumBrushes = Jalium.UI.Media.Brushes;
using JaliumColors = Jalium.UI.Media.Colors;
using JaliumCommandManager = Jalium.UI.Input.CommandManager;

namespace Jalium.UI.Tests;

public sealed class UtilityTypeShapeParityTests
{
    [Fact]
    public void CommandManagerUsesTheCanonicalSealedUtilityTypeShape()
    {
        AssertCanonicalUtilityType(
            typeof(JaliumCommandManager),
            nameof(JaliumCommandManager.InvalidateRequerySuggested),
            nameof(JaliumCommandManager.RequerySuggested),
            nameof(JaliumCommandManager.CanExecuteEvent));
    }

    [Fact]
    public void BrushesUsesTheCanonicalSealedUtilityTypeShape()
    {
        AssertCanonicalUtilityType(
            typeof(JaliumBrushes),
            nameof(JaliumBrushes.Black),
            nameof(JaliumBrushes.Transparent));
    }

    [Fact]
    public void ColorsUsesTheCanonicalSealedUtilityTypeShape()
    {
        AssertCanonicalUtilityType(
            typeof(JaliumColors),
            nameof(JaliumColors.Black),
            nameof(JaliumColors.Transparent));
    }

    private static void AssertCanonicalUtilityType(Type type, params string[] requiredStaticMembers)
    {
        Assert.True(type.IsPublic, type.FullName);
        Assert.True(type.IsClass, type.FullName);
        Assert.True(type.IsSealed, type.FullName);
        Assert.False(type.IsAbstract, type.FullName);

        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        ConstructorInfo constructor = Assert.Single(type.GetConstructors(
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.True(constructor.IsPrivate, type.FullName);
        Assert.Empty(constructor.GetParameters());

        MemberInfo[] publicApi = type
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(member => member.MemberType is
                MemberTypes.Event or
                MemberTypes.Field or
                MemberTypes.Method or
                MemberTypes.Property)
            .ToArray();

        Assert.NotEmpty(publicApi);
        Assert.All(publicApi, member =>
            Assert.True(IsStatic(member), $"{type.FullName}.{member.Name} must remain static."));

        foreach (string memberName in requiredStaticMembers)
        {
            Assert.NotEmpty(type.GetMember(
                memberName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        }
    }

    private static bool IsStatic(MemberInfo member)
    {
        return member switch
        {
            EventInfo @event => (@event.AddMethod ?? @event.RemoveMethod)?.IsStatic == true,
            FieldInfo field => field.IsStatic,
            MethodInfo method => method.IsStatic,
            PropertyInfo property => (property.GetMethod ?? property.SetMethod)?.IsStatic == true,
            _ => false,
        };
    }
}
