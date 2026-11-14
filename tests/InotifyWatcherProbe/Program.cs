using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;
using RoslynMcp.Roslyn.Services;

if (!OperatingSystem.IsLinux() || args.Length != 1)
    throw new ArgumentException("Usage on Linux: InotifyWatcherProbe <temporary directory>");

const int BatchSize = 10;
var root = Path.GetFullPath(args[0]);
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
using var emptyInotify = OpenEmptyInotify();
var baselineId = emptyInotify.DangerousGetHandle().ToInt32().ToString(CultureInfo.InvariantCulture);
var baseline = ReadDescriptors();
if (baseline.Count != 1 || !HasEmptyBaseline(baseline, baselineId))
    throw new InvalidOperationException($"Expected only the owned empty baseline descriptor {baselineId}. {Describe(baseline)}");

using var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
var ids = new List<string>();
try
{
    WatchBatch();
    var first = ReadDescriptors();
    WatchBatch();
    var second = ReadDescriptors();
    var marginal = second.Count - first.Count;
    if (!HasEmptyBaseline(first, baselineId) || !HasEmptyBaseline(second, baselineId) ||
        first.Count - baseline.Count < BatchSize || marginal != BatchSize)
    {
        throw new InvalidOperationException(
            $"Expected {BatchSize} first-batch workspace descriptors plus shared ancestors, then exactly {BatchSize} new descriptors. " +
            $"Baseline {Describe(baseline)}. First {Describe(first)}. Second {Describe(second)}.");
    }

    var owned = first.Keys.Concat(second.Keys)
        .Where(id => id != baselineId)
        .ToHashSet(StringComparer.Ordinal);
    UnwatchAll();

    // Dispose signals the Linux FileSystemWatcher read loop. Observe descriptor closure
    // before this process exits and before the still-live FileWatcherService is disposed.
    var remaining = ReadDescriptors();
    try
    {
        while (remaining.Count != baseline.Count || remaining.Keys.Any(owned.Contains) ||
               !HasEmptyBaseline(remaining, baselineId))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), deadline.Token);
            remaining = ReadDescriptors();
        }
    }
    catch (OperationCanceledException) when (deadline.IsCancellationRequested)
    {
        throw new InvalidOperationException(
            $"Unwatch did not release every owned descriptor before the deadline. " +
            $"Baseline {Describe(baseline)}. Remaining {Describe(remaining)}.");
    }

    Console.WriteLine(
        $"inotify footprint: baseline={baseline.Count} first={first.Count} second={second.Count} " +
        $"marginal={marginal} final={remaining.Count} owned={owned.Count} remainingOwned=0 emptyBaselineRetained=true");
}
finally
{
    UnwatchAll();
}

void WatchBatch()
{
    for (var i = 0; i < BatchSize; i++)
    {
        deadline.Token.ThrowIfCancellationRequested();
        var workspace = Path.Combine(root, $"ws{ids.Count}");
        Directory.CreateDirectory(workspace);
        var id = Guid.NewGuid().ToString("N");
        ids.Add(id);
        watcher.Watch(id, Path.Combine(workspace, "Sample.slnx"));
    }
}

void UnwatchAll()
{
    foreach (var id in ids)
        watcher.Unwatch(id);
    ids.Clear();
}

static SafeFileHandle OpenEmptyInotify()
{
    var descriptor = NativeMethods.InotifyInit(closeOnExec: NativeMethods.CloseOnExec);
    if (descriptor < 0)
        throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create the empty inotify baseline.");

    return new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
}

static bool HasEmptyBaseline(IReadOnlyDictionary<string, string> descriptors, string id) =>
    descriptors.TryGetValue(id, out var watches) && watches.Length == 0;

static Dictionary<string, string> ReadDescriptors()
{
    var descriptors = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var descriptorInfo in Directory.EnumerateFiles("/proc/self/fdinfo"))
    {
        try
        {
            var descriptor = Path.GetFileName(descriptorInfo);
            if (new FileInfo($"/proc/self/fd/{descriptor}").LinkTarget == "anon_inode:inotify")
            {
                var watches = File.ReadLines(descriptorInfo)
                    .Where(line => line.StartsWith("inotify ", StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal);
                descriptors.Add(descriptor, string.Join("; ", watches));
            }
        }
        catch (FileNotFoundException)
        {
            // The read loop closed the descriptor during this snapshot.
        }
    }

    return descriptors;
}

static string Describe(IReadOnlyDictionary<string, string> descriptors) =>
    $"count={descriptors.Count} [{string.Join(", ", descriptors.Select(pair => $"{pair.Key}={pair.Value}"))}]";

internal static class NativeMethods
{
    internal const int CloseOnExec = 0x80000;

    [DllImport("libc", EntryPoint = "inotify_init1", SetLastError = true)]
    internal static extern int InotifyInit(int closeOnExec);
}
