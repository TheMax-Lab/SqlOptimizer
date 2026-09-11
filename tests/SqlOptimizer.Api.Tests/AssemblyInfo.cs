using Xunit;

// Per-test configuration for these tests is supplied through process
// environment variables (a documented configuration source), which are
// process-global. Test collections must therefore run one at a time so no
// two factories observe each other's configuration.
[assembly: CollectionBehavior(DisableTestParallelization = true)]