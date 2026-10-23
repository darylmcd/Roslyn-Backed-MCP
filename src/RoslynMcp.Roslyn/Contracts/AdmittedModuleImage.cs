using System.Collections.Immutable;

namespace RoslynMcp.Roslyn.Contracts;

/// <summary>Owned PE bytes matched to one admitted Roslyn module; inspect metadata and IL without loading executable code.</summary>
/// <param name="FileName">Admitted physical basename, including the exact file alias used by the owning assembly manifest.</param>
/// <param name="ModuleName">Module-definition metadata name; it does not substitute for the manifest file alias.</param>
/// <param name="ModuleVersionId">Admitted module version identity.</param>
/// <param name="Image">Immutable bytes bound to the owned load.</param>
public sealed record AdmittedModuleImage(string FileName, string ModuleName, Guid ModuleVersionId, ImmutableArray<byte> Image);
