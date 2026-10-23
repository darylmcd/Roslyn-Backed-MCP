using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class EvaluatedGraphCancellationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task RestoreDetection_ObservesCallerCancellationAfterPathAuthorization(bool cancelCaller)
    {
        MsBuildInitializer.EnsureInitialized();
        return VerifyAsync(cancelCaller);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task VerifyAsync(bool cancelCaller)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(EvaluatedGraphCancellationTests), Guid.NewGuid().ToString("N"));
        AdhocWorkspace? workspace = null;
        Microsoft.Build.Evaluation.ProjectCollection? nativeProjects = null;
        CancellationTokenSource? cancellation = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "Malformed.csproj");
            const string malformed = "<Project><PropertyGroup>";
            await File.WriteAllTextAsync(path, malformed);
            Exception? nativeFailure = null;
            nativeProjects = new Microsoft.Build.Evaluation.ProjectCollection();
            try { nativeProjects.LoadProject(path); }
            catch (Exception failure) { nativeFailure = failure; }
            Assert.AreEqual("Microsoft.Build.Exceptions.InvalidProjectFileException", nativeFailure?.GetType().FullName,
                "The untouched physical definition must independently fail native evaluation before the actor is armed.");

            workspace = new AdhocWorkspace();
            var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
                "Malformed", "Malformed", LanguageNames.CSharp, filePath: path));
            cancellation = new CancellationTokenSource();
            var grants = 0;
            Task Authorize(IReadOnlyList<string> paths, CancellationToken token)
            {
                Assert.HasCount(1, paths);
                Assert.AreEqual(Path.GetFullPath(path), Path.GetFullPath(paths[0]));
                Assert.AreEqual(cancellation.Token, token);
                grants++;
                if (cancelCaller) cancellation.Cancel();
                return Task.CompletedTask;
            }

            Exception? observed = null;
            bool? result = null;
            try
            {
                result = await new RestoreStalenessDetector().DetectRestoreRequiredAsync(
                    project.Solution, Authorize, NullLogger.Instance, cancellation.Token);
            }
            catch (Exception failure) { observed = failure; }

            Assert.AreEqual(1, grants, "The actual selecting-path grant must execute.");
            Assert.AreEqual(cancelCaller, cancellation.IsCancellationRequested);
            Assert.AreEqual(malformed, await File.ReadAllTextAsync(path));
            CollectionAssert.AreEquivalent(new[] { path }, Directory.GetFiles(root, "*", SearchOption.AllDirectories));
            if (cancelCaller)
            {
                Assert.IsInstanceOfType<OperationCanceledException>(observed);
                Assert.AreEqual(cancellation.Token, ((OperationCanceledException)observed!).CancellationToken);
                Assert.IsNull(result);
            }
            else
            {
                Assert.IsNull(observed);
                Assert.AreEqual(false, result, "The unexpired control retains the documented unavailable-graph result.");
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native graph cancellation fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => cancellation?.Dispose()),
                CleanupFailureCollector.FromAction(() => workspace?.Dispose()),
                CleanupFailureCollector.FromAction(() => nativeProjects?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
