using Xunit;

namespace Jalium.UI.Tests;

[CollectionDefinition(AudioStatsExclusiveCollection.Name, DisableParallelization = true)]
public sealed class AudioStatsExclusiveCollection
{
    public const string Name = "AudioStatsExclusive";
}
