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
