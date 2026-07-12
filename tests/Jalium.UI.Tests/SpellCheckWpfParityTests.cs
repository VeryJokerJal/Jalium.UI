using System.Collections;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class SpellCheckWpfParityTests
{
    [Fact]
    public void InstanceProperties_AreBackedByTheOwningTextBox()
    {
        var textBox = new TextBox();
        SpellCheck spellCheck = textBox.SpellCheck;

        spellCheck.IsEnabled = true;
        spellCheck.SpellingReform = SpellingReform.Prereform;
        spellCheck.CustomDictionaries.Add(new Uri("file:///custom.lex"));

        Assert.True(SpellCheck.GetIsEnabled(textBox));
        Assert.True(textBox.IsSpellCheckEnabled);
        Assert.Equal(SpellingReform.Prereform, SpellCheck.GetSpellingReform(textBox));
        Assert.Same(spellCheck.CustomDictionaries, textBox.SpellCheck.CustomDictionaries);
        Assert.Single(spellCheck.CustomDictionaries);
    }

    [Fact]
    public void CustomDictionaries_AreCreatedPerTextBox()
    {
        var first = new TextBox();
        var second = new TextBox();

        IList firstDictionaries = SpellCheck.GetCustomDictionaries(first);
        IList secondDictionaries = SpellCheck.GetCustomDictionaries(second);

        Assert.NotSame(firstDictionaries, secondDictionaries);
        Assert.True(SpellCheck.CustomDictionariesProperty.ReadOnly);
    }

    [Fact]
    public void StaticAccessors_RequireTextBoxBase()
    {
        Assert.Equal(typeof(TextBoxBase), typeof(SpellCheck).GetMethod(nameof(SpellCheck.GetIsEnabled))!.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(TextBoxBase), typeof(SpellCheck).GetMethod(nameof(SpellCheck.SetIsEnabled))!.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(TextBoxBase), typeof(SpellCheck).GetMethod(nameof(SpellCheck.GetCustomDictionaries))!.GetParameters()[0].ParameterType);

        Assert.Throws<ArgumentNullException>(() => SpellCheck.GetIsEnabled(null!));
        Assert.Throws<ArgumentNullException>(() => SpellCheck.SetIsEnabled(null!, true));
        Assert.Throws<ArgumentNullException>(() => SpellCheck.GetCustomDictionaries(null!));
    }

    [Fact]
    public void LegacyTextBoxProperty_StaysSynchronized()
    {
        var textBox = new TextBox();

        textBox.IsSpellCheckEnabled = true;
        Assert.True(textBox.SpellCheck.IsEnabled);

        textBox.SpellCheck.IsEnabled = false;
        Assert.False(textBox.IsSpellCheckEnabled);
    }
}
