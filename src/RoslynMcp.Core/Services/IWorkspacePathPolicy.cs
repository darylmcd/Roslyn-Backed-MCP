namespace RoslynMcp.Core.Services;

/// <summary>Approves each explicit workspace input and destination inventory before service filesystem operations.</summary>
public interface IWorkspacePathPolicy
{
    /// <summary>Validates current host and request boundaries. Paths must already identify physical canonical targets.</summary>
    /// <remarks>Native MSBuild evaluation remains trusted project-code execution; this policy does not sandbox it.</remarks>
    Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct);
}
