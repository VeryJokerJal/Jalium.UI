using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

// The single definition of the "Application" collection: members share
// process-global state (theme loader, AnimationManager, Dispatcher) so they
// must not run in parallel with each other. Keep exactly one
// CollectionDefinition for this name — with a duplicate, xUnit picks one
// arbitrarily and can silently drop DisableParallelization.
[CollectionDefinition("Application", DisableParallelization = true)]
public sealed class ApplicationCollection : ICollectionFixture<ApplicationCollectionFixture>
{
}

public sealed class ApplicationCollectionFixture
{
    public ApplicationCollectionFixture()
    {
        ThemeLoader.Initialize();
    }
}
