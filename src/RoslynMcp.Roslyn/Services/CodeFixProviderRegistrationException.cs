namespace RoslynMcp.Roslyn.Services;

/// <summary>Internal failure raised when a code-fix provider cannot register its actions.</summary>
internal sealed class CodeFixProviderRegistrationException(Exception innerException)
    : Exception("A code-fix provider failed while registering actions.", innerException);
