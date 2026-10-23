using System.Collections.Immutable;

namespace RoslynMcp.Core.Services;

/// <summary>Immutable, explicitly resolved dotnet configuration inventory for a prepared test invocation.</summary>
public sealed record PreparedDotnetTestContext(ImmutableArray<DotnetConfigurationIdentity> Candidates, bool UsesNativeMtp,
    ImmutableArray<DotnetConfigurationIdentity> ExecutionInputs)
{
    public string? GlobalJsonPath => Candidates.IsDefaultOrEmpty ? null : Candidates.FirstOrDefault(candidate => candidate.Exists)?.FilePath;
    public override string ToString() => $"PreparedDotnetTestContext {{ CandidateCount = {(Candidates.IsDefault ? 0 : Candidates.Length)}, UsesNativeMtp = {UsesNativeMtp} }}";
}

/// <summary>A configuration candidate observed only after the current workspace policy approved its path.</summary>
public sealed record DotnetConfigurationIdentity(string FilePath, bool Exists, ImmutableArray<byte> ContentHash);
