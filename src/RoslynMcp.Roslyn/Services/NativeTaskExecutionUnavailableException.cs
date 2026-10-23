namespace RoslynMcp.Roslyn.Services;

/// <summary>Explicit missing execution proof; never substitutes for an unexpected producer failure.</summary>
internal sealed class NativeTaskExecutionUnavailableException() : InvalidOperationException(
    "Native task execution implementation metadata is unavailable; retain semantic loading without compiler or physical mutation authority.");

/// <summary>A checked native metadata shape is unavailable in the owned child.</summary>
internal sealed class NativeTaskExecutionMetadataUnavailableException() : InvalidOperationException(
    "The native execution metadata shape cannot be verified.");
