namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a dependency injection registration discovered in source code.
/// </summary>
public sealed record DiRegistrationDto(
    string ServiceType,
    string ImplementationType,
    string Lifetime,
    string FilePath,
    int Line,
    string RegistrationMethod);

/// <summary>
/// Per-registration entry inside an override chain. <see cref="EffectiveStatus"/> describes
/// how MS.DI's descriptor resolution treats this call given the order of all registrations
/// for the same service type:
/// <list type="bullet">
///   <item><description><c>winning</c> — the last <c>Add*</c> descriptor; what
///   <c>GetService&lt;TService&gt;()</c> returns.</description></item>
///   <item><description><c>overridden</c> — an earlier <c>Add*</c> descriptor that a later
///   <c>Add*</c> displaces (last-wins).</description></item>
///   <item><description><c>shadowed</c> — a <c>TryAdd*</c> call that was a no-op because a
///   descriptor for the same service type already existed (first-wins variant short-circuited).
///   </description></item>
/// </list>
/// di-lifetime-mismatch-detection.
/// </summary>
public sealed record DiRegistrationOverrideEntryDto(
    string ImplementationType,
    string Lifetime,
    string FilePath,
    int Line,
    string RegistrationMethod,
    string EffectiveStatus);

/// <summary>
/// One service type's full override chain. <see cref="LifetimesDiffer"/> is true when the
/// chain mixes lifetimes (e.g. <c>Singleton</c> and <c>Scoped</c>) — a captive-dependency or
/// dead-shadowing risk. <see cref="DeadRegistrationCount"/> is the count of entries whose
/// <c>EffectiveStatus</c> is <c>overridden</c> or <c>shadowed</c>.
/// di-lifetime-mismatch-detection.
/// </summary>
public sealed record DiRegistrationOverrideChainDto(
    string ServiceType,
    IReadOnlyList<DiRegistrationOverrideEntryDto> Registrations,
    string WinningLifetime,
    string WinningImplementationType,
    bool LifetimesDiffer,
    int DeadRegistrationCount);

/// <summary>
/// Composite scan result returned when the caller opts in to override-chain analysis. The
/// <see cref="Registrations"/> list is the same shape returned by the legacy
/// <c>GetDiRegistrationsAsync</c> path; <see cref="OverrideChains"/> groups any service type
/// with two or more registrations into a chain entry.
/// di-lifetime-mismatch-detection.
/// </summary>
public sealed record DiRegistrationScanResult(
    IReadOnlyList<DiRegistrationDto> Registrations,
    IReadOnlyList<DiRegistrationOverrideChainDto> OverrideChains);
