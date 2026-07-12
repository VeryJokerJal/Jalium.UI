using Xunit;

// The suite exercises process-wide framework state (Dispatcher queues, theme and
// resource singletons, native decoder hooks, render targets, and audio telemetry).
// Running independent xUnit collections concurrently lets one test replace or
// dispose state that another test is still observing, producing order-dependent
// failures and occasional native access violations. Keep the assembly serial so a
// full-suite result represents the same isolation used by the focused tests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
