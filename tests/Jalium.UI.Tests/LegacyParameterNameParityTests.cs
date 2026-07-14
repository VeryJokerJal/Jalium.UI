using System.Reflection;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class LegacyParameterNameParityTests
{
    [Theory]
    [InlineData(typeof(ValueSource), "op_Equality", "vs1", "vs2")]
    [InlineData(typeof(ValueSource), "op_Inequality", "vs1", "vs2")]
    [InlineData(typeof(LocalValueEnumerator), "op_Equality", "obj1", "obj2")]
    [InlineData(typeof(LocalValueEnumerator), "op_Inequality", "obj1", "obj2")]
    [InlineData(typeof(CustomPopupPlacement), "op_Equality", "placement1", "placement2")]
    [InlineData(typeof(CustomPopupPlacement), "op_Inequality", "placement1", "placement2")]
    [InlineData(typeof(RoutedEventHandlerInfo), "op_Equality", "handlerInfo1", "handlerInfo2")]
    [InlineData(typeof(RoutedEventHandlerInfo), "op_Inequality", "handlerInfo1", "handlerInfo2")]
    public void OperatorParameterNamesMatchWpf(
        Type type,
        string methodName,
        string firstName,
        string secondName)
    {
        MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;
        Assert.Equal(new[] { firstName, secondName }, method.GetParameters().Select(parameter => parameter.Name));
    }
}
