using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public sealed class SpellingErrorParityTests
{
    [Fact]
    public void SuggestionsUseEnumerableAndOperationsDelegateToOwningEditor()
    {
        Assert.Equal(
            typeof(IEnumerable<string>),
            typeof(SpellingError).GetProperty(nameof(SpellingError.Suggestions))!.PropertyType);

        var original = new SpellingError(
            2,
            4,
            "misp",
            SpellingErrorType.GetSuggestions,
            null,
            new[] { "miss", "mist" });
        SpellingError? correctedError = null;
        string? correction = null;
        SpellingError? ignoredError = null;
        var bound = original.WithHandlers(
            (error, text) =>
            {
                correctedError = error;
                correction = text;
            },
            error => ignoredError = error);

        bound.Correct("miss");
        bound.IgnoreAll();

        Assert.Same(bound, correctedError);
        Assert.Equal("miss", correction);
        Assert.Same(bound, ignoredError);
        Assert.Equal(new[] { "miss", "mist" }, bound.Suggestions);
        Assert.Throws<ArgumentNullException>(() => bound.Correct(null!));
    }
}
