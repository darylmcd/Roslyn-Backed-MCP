using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Checks actual task-bound paths before known native SDK effects.</summary>
public sealed class AdmitForkBuildPaths : ForkAdmissionTask
{
    [Required] public string ProjectPath { get; set; } = string.Empty;
    [Required] public string Operation { get; set; } = string.Empty;
    public ITaskItem[] Inputs { get; set; } = [];
    public ITaskItem[] Outputs { get; set; } = [];
    public ITaskItem[] Content { get; set; } = [];
    public string OriginalRoot { get; set; } = string.Empty;
    public string StageRoot { get; set; } = string.Empty;
    public string DestinationFolder { get; set; } = string.Empty;
    [Output] public string NewStageRoot { get; private set; } = string.Empty;

    private protected override void ExecuteAdmission(ForkAdmissionMaterial.Configuration config)
    {
        var admission = new ForkPathAdmission(config.SourceRoot, config.ForkRoot, ProjectPath, CheckCancellation);
        switch (Operation)
        {
            case "Context":
                if (BuildEngine is not IBuildEngine6 engine)
                    throw InvalidOperationErrors.Internal("Native fork admission requires actual engine globals.");
                using (var collection = new ProjectCollection())
                {
                    var project = new Project(ProjectPath, new Dictionary<string, string>(engine.GetGlobalProperties()), null, collection);
                    ForkGraphPreparation.Inspect(project.CreateProjectInstance(), config.SourceRoot, config.ForkRoot,
                        () => { CheckCancellation(); return TimeSpan.MaxValue; }, CancellationToken.None);
                }
                break;
            case "Paths":
            case "CopyToFolder":
                foreach (var item in Inputs) admission.Check(item.ItemSpec, "native input", normalizeNativeOperation: true);
                foreach (var item in Outputs) admission.Check(item.ItemSpec, "native output", normalizeNativeOperation: true);
                if (Operation == "CopyToFolder")
                    foreach (var item in Inputs)
                        admission.Check(Path.Combine(DestinationFolder, Path.GetFileName(item.ItemSpec)), "native output", normalizeNativeOperation: true);
                break;
            case "PreprocessStage":
                Preflight(admission);
                break;
            case "PreprocessOutputs":
                AdmitPreprocessed(admission);
                break;
            default:
                throw InvalidOperationErrors.Internal("An unknown native fork admission operation was requested.");
        }
    }

    private void Preflight(ForkPathAdmission admission)
    {
        NewStageRoot = Path.Combine(ExecutionRoot, "stages", Guid.NewGuid().ToString("N"));
        if (!FileSystemPath.Comparer.Equals(NewStageRoot, PhysicalPathResolver.Resolve(NewStageRoot)) ||
            Directory.Exists(NewStageRoot) || File.Exists(NewStageRoot))
            throw InvalidOperationErrors.Internal("Native preprocessing requires a fresh unaliased stage directory.");

        foreach (var item in Content)
        {
            CheckCancellation();
            admission.Check(item.ItemSpec, "preprocessing input", normalizeNativeOperation: true);
            var output = item.GetMetadata("PPOutputPath");
            if (string.IsNullOrEmpty(output)) continue;
            // The native preprocessor contributes one opaque regular hash component.
            // Only component depth matters here; the SDK computes the actual hash.
            var expression = Path.Combine(NewStageRoot, "OPAQUE_HASH", item.GetMetadata("NuGetPackageId"),
                item.GetMetadata("NuGetPackageVersion"), output);
            admission.Check(expression, "preprocessing staging output", normalizeNativeOperation: true);
        }
    }

    private void AdmitPreprocessed(ForkPathAdmission admission)
    {
        var baseDirectory = Path.GetDirectoryName(ProjectPath)
            ?? throw InvalidOperationErrors.Internal("A native preprocessing project has no directory.");
        var stage = Path.GetFullPath(StageRoot, baseDirectory);
        var direct = Content.Select(item => Path.Combine("OPAQUE_HASH", item.GetMetadata("NuGetPackageId"),
                item.GetMetadata("NuGetPackageVersion"), item.GetMetadata("PPOutputPath")))
            .Where(Path.IsPathFullyQualified).Select(path => Path.GetFullPath(path, baseDirectory)).ToHashSet(FileSystemPath.Comparer);
        foreach (var output in Outputs.Select(item => Path.GetFullPath(item.ItemSpec, baseDirectory)).Distinct(FileSystemPath.Comparer))
        {
            CheckCancellation();
            var original = direct.Contains(output) ? output
                : Path.GetFullPath(Path.Combine(OriginalRoot, Path.GetRelativePath(stage, output)), baseDirectory);
            admission.Check(original, "native preprocessing output", normalizeNativeOperation: true);
        }
    }

}
