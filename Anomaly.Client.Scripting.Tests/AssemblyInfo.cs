using Xunit;

// The privately named engine still has process-global platform/registration settings.
// Keep these security regressions independent of simultaneous mutations and CPU load.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
