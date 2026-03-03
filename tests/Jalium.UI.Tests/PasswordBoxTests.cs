using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class PasswordBoxTests
{
    [Fact]
    public void PasswordBox_TextTrimming_DefaultsToCharacterEllipsis()
    {
        // Arrange & Act
        var passwordBox = new PasswordBox();

        // Assert
        Assert.Equal(TextTrimming.CharacterEllipsis, passwordBox.TextTrimming);
    }

    [Fact]
    public void PasswordBox_Placeholder_DefaultsToEmpty()
    {
        // Arrange & Act
        var passwordBox = new PasswordBox();

        // Assert
        Assert.Equal(string.Empty, passwordBox.PlaceholderText);
    }

    [Fact]
    public void PasswordBox_Placeholder_ShouldBeSettable()
    {
        // Arrange
        var passwordBox = new PasswordBox();

        // Act
        passwordBox.PlaceholderText = "Enter password";

        // Assert
        Assert.Equal("Enter password", passwordBox.PlaceholderText);
    }
}
