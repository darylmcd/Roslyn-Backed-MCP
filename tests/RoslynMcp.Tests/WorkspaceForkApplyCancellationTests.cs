using System.Reflection;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for backlog row <c>workspace-fork-apply-robustness-cancellation</c>:
/// <see cref="ValidationBundleTools"/>'s private <c>CopyDirectory</c> /
/// <c>DeleteDirectoryIfExists</c> recursive walks were synchronous and non-cancellable —
/// a cancellation token passed to <c>workspace_fork_apply</c> was silently dropped once
/// inside the copy/cleanup loops. These tests exercise the two helpers directly via
/// reflection (an established pattern in this suite — see
/// <c>DeadLoggerFieldsTests</c>/<c>ToolDiResolutionTests</c>) so the cancellation contract
/// can be pinned without standing up the full <c>workspace_fork_apply</c> pipeline
/// (real Roslyn workspace load + <c>dotnet restore</c>), and also pins the per-source-root
/// <c>SemaphoreSlim</c> keying used to serialize concurrent fork-apply calls.
/// </summary>
[TestClass]
public sealed class WorkspaceForkApplyCancellationTests
{
    private static readonly Type ToolsType = typeof(ValidationBundleTools);

    private static readonly MethodInfo CopyDirectoryMethod = ToolsType.GetMethod(
        "CopyDirectory", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("CopyDirectory method not found via reflection.");

    private static readonly MethodInfo DeleteDirectoryIfExistsMethod = ToolsType.GetMethod(
        "DeleteDirectoryIfExists", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("DeleteDirectoryIfExists method not found via reflection.");

    private static readonly FieldInfo ForkApplyLocksField = ToolsType.GetField(
        "ForkApplyLocks", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ForkApplyLocks field not found via reflection.");

    private string _tempRoot = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "roslyn-mcp-forkapply-cancel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    [TestMethod]
    public void CopyDirectory_NotCancelled_CopiesAllFilesAndSubdirectoriesUnchanged()
    {
        var sourceDir = Path.Combine(_tempRoot, "source");
        var destDir = Path.Combine(_tempRoot, "dest");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(sourceDir, "b.txt"), "b");
        var nested = Path.Combine(sourceDir, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "c.txt"), "c");

        InvokeCopyDirectory(sourceDir, destDir, CancellationToken.None);

        Assert.AreEqual("a", File.ReadAllText(Path.Combine(destDir, "a.txt")));
        Assert.AreEqual("b", File.ReadAllText(Path.Combine(destDir, "b.txt")));
        Assert.AreEqual("c", File.ReadAllText(Path.Combine(destDir, "nested", "c.txt")));
    }

    [TestMethod]
    public void CopyDirectory_CancelledBeforeWalk_ThrowsOperationCanceledAndCopiesNothing()
    {
        var sourceDir = Path.Combine(_tempRoot, "source");
        var destDir = Path.Combine(_tempRoot, "dest");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(sourceDir, "b.txt"), "b");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var thrown = Assert.ThrowsExactly<TargetInvocationException>(
            () => InvokeCopyDirectory(sourceDir, destDir, cts.Token));
        Assert.IsInstanceOfType<OperationCanceledException>(thrown.InnerException,
            "Cancellation must surface as OperationCanceledException, not be silently swallowed by the copy loop.");
        Assert.IsFalse(File.Exists(Path.Combine(destDir, "a.txt")),
            "A cancelled copy must not proceed to copy files — the check is at the top of each loop iteration.");
    }

    [TestMethod]
    public void DeleteDirectoryIfExists_NotCancelled_DeletesReadOnlyFilesAndDirectory()
    {
        var targetDir = Path.Combine(_tempRoot, "todelete");
        Directory.CreateDirectory(targetDir);
        var filePath = Path.Combine(targetDir, "readonly.txt");
        File.WriteAllText(filePath, "content");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        InvokeDeleteDirectoryIfExists(targetDir, CancellationToken.None);

        Assert.IsFalse(Directory.Exists(targetDir), "Existing behavior: read-only files must still be deletable.");
    }

    [TestMethod]
    public void DeleteDirectoryIfExists_CancelledBeforeWalk_ThrowsOperationCanceledAndLeavesDirectoryIntact()
    {
        var targetDir = Path.Combine(_tempRoot, "todelete-cancelled");
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(Path.Combine(targetDir, "file.txt"), "content");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var thrown = Assert.ThrowsExactly<TargetInvocationException>(
            () => InvokeDeleteDirectoryIfExists(targetDir, cts.Token));
        Assert.IsInstanceOfType<OperationCanceledException>(thrown.InnerException);
        Assert.IsTrue(Directory.Exists(targetDir),
            "A cancelled delete must not proceed to delete the directory.");
    }

    [TestMethod]
    public void ForkApplyLocks_SameNormalizedSourceRoot_SharesOneSemaphoreAcrossConcurrentCallers()
    {
        // Mirrors exactly what WorkspaceForkApply does: ForkApplyLocks.GetOrAdd(Path.GetFullPath(sourceRoot), ...).
        // Same source root (even with different casing / relative segments that normalize to the
        // same full path) must resolve to the same SemaphoreSlim instance so concurrent
        // workspace_fork_apply calls against it are serialized rather than racing.
        var locks = (System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>)
            ForkApplyLocksField.GetValue(null)!;

        var sourceRoot = Path.Combine(_tempRoot, "SourceRoot");
        Directory.CreateDirectory(sourceRoot);
        var variantCasing = Path.Combine(_tempRoot, "sourceroot");

        var lock1 = locks.GetOrAdd(Path.GetFullPath(sourceRoot), _ => new SemaphoreSlim(1, 1));
        var lock2 = locks.GetOrAdd(Path.GetFullPath(sourceRoot), _ => new SemaphoreSlim(1, 1));
        Assert.AreSame(lock1, lock2, "Identical source root must resolve to the same semaphore instance.");

        var otherRoot = Path.Combine(_tempRoot, "OtherRoot");
        Directory.CreateDirectory(otherRoot);
        var lock3 = locks.GetOrAdd(Path.GetFullPath(otherRoot), _ => new SemaphoreSlim(1, 1));
        Assert.AreNotSame(lock1, lock3, "A distinct source root must not share the same semaphore.");

        // The dictionary itself is case-insensitive (Windows path semantics), matching how
        // ValidationBundleTools constructs it.
        Assert.IsTrue(locks.TryGetValue(variantCasing, out var caseInsensitiveHit),
            "ForkApplyLocks must be keyed case-insensitively so 'SourceRoot' and 'sourceroot' collide on Windows.");
        Assert.AreSame(lock1, caseInsensitiveHit);
    }

    private static void InvokeCopyDirectory(string sourceDir, string destinationDir, CancellationToken ct) =>
        CopyDirectoryMethod.Invoke(null, [sourceDir, destinationDir, ct]);

    private static void InvokeDeleteDirectoryIfExists(string path, CancellationToken ct) =>
        DeleteDirectoryIfExistsMethod.Invoke(null, [path, ct]);
}
