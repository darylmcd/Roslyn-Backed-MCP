namespace Company.RoslynMcp.Roslyn.Services;

public sealed class ValidationServiceOptions
{
    public TimeSpan BuildTimeout { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan TestTimeout { get; init; } = TimeSpan.FromMinutes(10);

    public int MaxRelatedFiles { get; init; } = 25;
}
