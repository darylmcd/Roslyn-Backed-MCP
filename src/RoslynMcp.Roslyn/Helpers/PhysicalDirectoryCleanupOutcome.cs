namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Reports physical removal separately from safely retained directory names.</summary>
internal enum PhysicalDirectoryCleanupOutcome
{
    Removed,
    Retained
}
