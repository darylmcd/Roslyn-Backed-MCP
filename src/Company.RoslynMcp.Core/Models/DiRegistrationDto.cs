namespace Company.RoslynMcp.Core.Models;

public sealed record DiRegistrationDto(
    string ServiceType,
    string ImplementationType,
    string Lifetime,
    string FilePath,
    int Line,
    string RegistrationMethod);
