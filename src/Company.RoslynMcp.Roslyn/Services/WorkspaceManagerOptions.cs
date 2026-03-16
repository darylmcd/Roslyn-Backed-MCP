namespace Company.RoslynMcp.Roslyn.Services;

public sealed class WorkspaceManagerOptions
{
    public int MaxConcurrentWorkspaces { get; init; } = 8;

    public int MaxSourceGeneratedDocuments { get; init; } = 500;
}
