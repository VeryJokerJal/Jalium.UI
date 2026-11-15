using Xunit;

// These UI contracts share process-wide theme dictionaries and dispatcher state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
