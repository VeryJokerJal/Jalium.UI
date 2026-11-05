using Xunit;

// Geometry and shutdown fixtures replace or dispose process-wide rendering,
// dispatcher, theme and resource state. Keep the macOS assembly serial, as the
// shared test assembly already does, so other tests cannot change text metrics
// while a layout assertion is in progress.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Jalium.UI.Tests;

// Shared input contracts use the same collection name in the Windows suite.
[CollectionDefinition("ParityFoundationBehaviorCollection", DisableParallelization = true)]
public sealed class MacOSParityFoundationBehaviorCollection;
