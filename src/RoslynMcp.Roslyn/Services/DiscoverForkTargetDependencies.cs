using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Orders admission after the effective native consumer prerequisites and authored hooks.</summary>
public sealed class DiscoverForkTargetDependencies : ForkAdmissionTask
{
    [Required]
    public string ProjectPath { get; set; } = string.Empty;

    [Required]
    public string ConsumerTarget { get; set; } = string.Empty;

    [Output]
    public string[] DependencyTargets { get; private set; } = [];

    private protected override void ExecuteAdmission(ForkAdmissionMaterial.Configuration configuration)
    {
        if (BuildEngine is not IBuildEngine6 engine)
        {
            throw RoslynMcp.Core.Services.InvalidOperationErrors.Internal("Fork admission requires IBuildEngine6 global-property access.");
        }

        using var collection = new ProjectCollection();
        var project = new Project(ProjectPath, new Dictionary<string, string>(engine.GetGlobalProperties()), null, collection);
        if (!project.Targets.TryGetValue(ConsumerTarget, out var consumer))
        {
            throw RoslynMcp.Core.Services.InvalidOperationErrors.Internal("The native fork admission consumer target could not be inspected.");
        }

        CheckCancellation();
        var dependencies = new List<string>();
        dependencies.AddRange(Split(project.ExpandString(consumer.DependsOnTargets)));
        foreach (var element in project.GetLogicalProject().OfType<ProjectTargetElement>())
        {
            CheckCancellation();
            if (RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer.Equals(element.Location.File, configuration.GuardTargetsPath) ||
                !project.Targets.TryGetValue(element.Name, out var effective) ||
                effective.Location.File != element.Location.File || effective.Location.Line != element.Location.Line)
                continue;

            if (Split(project.ExpandString(effective.BeforeTargets)).Contains(ConsumerTarget, StringComparer.OrdinalIgnoreCase))
                dependencies.Add(element.Name);
        }

        DependencyTargets = dependencies.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] Split(string value) =>
        value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
