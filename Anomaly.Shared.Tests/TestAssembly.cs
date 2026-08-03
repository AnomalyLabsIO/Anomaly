using Xunit;

// The linked production guards and transfer managers intentionally own static
// process-wide state. Serial execution keeps adversarial reset/flood tests
// deterministic and mirrors the single server process that owns that state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
