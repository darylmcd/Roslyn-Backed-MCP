using System.Globalization;
using Microsoft.Build.Framework;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Shares invocation ownership, cancellation and refusal transport for native admission tasks.</summary>
public abstract class ForkAdmissionTask : Microsoft.Build.Utilities.Task, ICancelableTask
{
    private volatile bool _cancelled;

    [Required] public string ConfigPath { get; set; } = string.Empty;
    [Required] public string ExecutionRoot { get; set; } = string.Empty;
    public string DeadlineUtc { get; set; } = string.Empty;

    public void Cancel() => _cancelled = true;

    public sealed override bool Execute()
    {
        var executionVerified = false;
        try
        {
            var config = ForkAdmissionMaterial.Read(ConfigPath);
            var executions = Path.Combine(config.ForkRoot, ForkAdmissionMaterial.DirectoryName, "executions");
            if (string.IsNullOrWhiteSpace(ExecutionRoot) ||
                !FileSystemPath.IsStrictDescendant(PhysicalPathResolver.Resolve(executions), PhysicalPathResolver.Resolve(ExecutionRoot)) ||
                !Directory.Exists(ExecutionRoot))
                throw InvalidOperationErrors.Internal("A native fork invocation has no valid owned execution directory.");
            executionVerified = true;
            CheckCancellation();
            ExecuteAdmission(config);
            CheckCancellation();
            return true;
        }
        catch (Exception ex)
        {
            // BuildHost may return a partial clean workspace after a task error.
            // Keep refusal independently of its bounded diagnostic transport.
            try
            {
                if (executionVerified && Directory.Exists(ExecutionRoot))
                    File.WriteAllText(Path.Combine(ExecutionRoot, "refused-" + Guid.NewGuid().ToString("N")), ex.GetType().Name);
            }
            catch (Exception latchFailure) when (latchFailure is IOException or UnauthorizedAccessException)
            { Log.LogError("Native fork admission could not preserve its refusal: {0}.", latchFailure.GetType().Name); }
            Log.LogError("Native fork paths were refused before the SDK effect: {0}.", ex.GetType().Name);
            return false;
        }
    }

    private protected abstract void ExecuteAdmission(ForkAdmissionMaterial.Configuration configuration);

    protected void CheckCancellation()
    {
        if (_cancelled || File.Exists(Path.Combine(ExecutionRoot, "cancelled")))
            throw new OperationCanceledException("Native fork admission was cancelled.");
        if (!string.IsNullOrEmpty(DeadlineUtc))
        {
            if (!DateTimeOffset.TryParse(DeadlineUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var deadline))
                throw InvalidOperationErrors.Internal("A native fork invocation deadline is invalid.");
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("Native fork admission exceeded its owning invocation deadline.");
        }
    }
}
