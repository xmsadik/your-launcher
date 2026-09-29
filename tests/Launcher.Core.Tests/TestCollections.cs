namespace YourLauncher.Core.Tests;

/// <summary>
/// Tests that change process-wide environment variables (PATH/PATHEXT). xUnit runs test classes in
/// parallel, so two such classes would overwrite each other's PATH mid-test; a non-parallel collection
/// runs them one at a time, after all the parallel tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "Process environment";
}

/// <summary>Wall-clock timing tests: run alone so other tests' CPU load can't push them over budget.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Timing";
}
