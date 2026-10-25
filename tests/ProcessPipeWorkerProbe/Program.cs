using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Text;

if (args[0] is "hold" or "exit" or "large" or "formatter-output")
{
    File.WriteAllText(args[1] + ".tmp", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    File.Move(args[1] + ".tmp", args[1]);
    if (args[0] is "hold" or "formatter-output")
    {
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(args[2]) && deadline.Elapsed < TimeSpan.FromSeconds(60))
            Thread.Sleep(10);
        if (!File.Exists(args[2]))
            return 91;
        if (args[0] == "hold")
            return 0;
        var stdout = Console.OpenStandardOutput();
        var stderr = Console.OpenStandardError();
        stdout.WriteByte((byte)'o');
        stdout.Flush();
        stderr.WriteByte((byte)'e');
        stderr.Flush();
        deadline.Restart();
        while (!File.Exists(args[2] + ".tail") && deadline.Elapsed < TimeSpan.FromSeconds(60))
            Thread.Sleep(10);
        if (!File.Exists(args[2] + ".tail"))
            return 92;
        // Exercise the actual incremental decoder across separately written UTF-8 bytes.
        foreach (var (stream, text) in new[]
        {
            (stdout, "ut:alpha\r\nβ🙂tail"),
            (stderr, "rr:alpha\r\nβ🙂tail")
        })
        {
            foreach (var value in Encoding.UTF8.GetBytes(text))
            {
                stream.WriteByte(value);
                stream.Flush();
                Thread.Sleep(15);
            }
            // The trailing incomplete scalar must be flushed by the actual capture decoder.
            stream.WriteByte(0xf0);
            stream.WriteByte(0x9f);
            stream.Flush();
        }
        return 0;
    }

    Console.Error.Write(args[0] == "large" ? new string('e', 65536) : "stderr-sentinel");
    Console.Out.Write(args[0] == "large" ? new string('o', 65536) : "stdout-sentinel");
    return 37;
}

var assemblyPath = Path.GetFullPath(args[0]);
var root = Path.GetFullPath(args[1]);
var scenario = args[2];
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(Path.GetDirectoryName(assemblyPath)!, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
var runner = Assembly.LoadFrom(assemblyPath).GetType("RoslynMcp.Tests.Helpers.PwshScriptRunner", throwOnError: true)!;
if (scenario is "formatter-capture" or "formatter-capture-cleanup")
    return RunFormatterCaptureProbe(assemblyPath, root, failRelease: scenario == "formatter-capture-cleanup");
var method = runner.GetMethod("RunExecutableAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
var workers = scenario == "held-pipes" ? 2 : 1;
ThreadPool.SetMinThreads(workers, workers);
if (!ThreadPool.SetMaxThreads(workers, workers))
    throw new InvalidOperationException("Cannot establish the isolated worker limit.");

var release = Path.Combine(root, "release");
Process? heldChild = null;
Task? holder = null;
Task? execution = null;
Task Invoke(string mode, string pidPath) => (Task)method.Invoke(null,
    ["dotnet", new[] { Assembly.GetExecutingAssembly().Location, mode, pidPath, release },
     null, root, TimeSpan.FromSeconds(30), CancellationToken.None, mode + " output-worker probe"])!;
try
{
    if (scenario == "held-pipes")
    {
        var holderPid = Path.Combine(root, "holder.pid");
        holder = Invoke("hold", holderPid);
        WaitForFile(holderPid);
        heldChild = Process.GetProcessById(int.Parse(File.ReadAllText(holderPid), System.Globalization.CultureInfo.InvariantCulture));
        if (heldChild.HasExited)
            throw new InvalidOperationException("The competing pipe owner exited before the probe.");
    }

    var pidPath = Path.Combine(root, "output.pid");
    var mode = scenario == "held-pipes" ? "exit" : "large";
    execution = Invoke(mode, pidPath);
    WaitForFile(pidPath);
    bool nativeExited;
    try
    {
        using var child = Process.GetProcessById(int.Parse(File.ReadAllText(pidPath), System.Globalization.CultureInfo.InvariantCulture));
        nativeExited = child.WaitForExit(5_000);
    }
    catch (ArgumentException)
    {
        nativeExited = true; // The owned child exited before the process lookup.
    }
    var managedCompleted = execution.Wait(TimeSpan.FromSeconds(5));
    if (!nativeExited || !managedCompleted)
        throw new InvalidOperationException(
            $"Redirected output blocked: nativeExited={nativeExited}, managedCompleted={managedCompleted}, queued={ThreadPool.PendingWorkItemCount}, workers={ThreadPool.ThreadCount}.");

    if (heldChild?.HasExited == true)
        throw new InvalidOperationException("The competing pipe owner must remain alive until the probe finishes.");

    var result = execution.GetType().GetProperty("Result")!.GetValue(execution)!;
    object? Get(string name) => result.GetType().GetProperty(name)!.GetValue(result);
    if ((int)Get("ExitCode")! != 37 ||
        (string)Get("StdOut")! != (mode == "large" ? new string('o', 65536) : "stdout-sentinel") ||
        (string)Get("StdErr")! != (mode == "large" ? new string('e', 65536) : "stderr-sentinel"))
        throw new InvalidOperationException("The worker probe lost output or changed the child exit code.");
    return 0;
}
finally
{
    File.WriteAllText(release, "release");
    ThreadPool.SetMaxThreads(32, 32);
    // Both children are owned by the runner; always release the held pipe and observe cleanup.
    try { holder?.GetAwaiter().GetResult(); }
    finally
    {
        try { execution?.GetAwaiter().GetResult(); }
        finally { heldChild?.Dispose(); }
    }
}

static void WaitForFile(string path)
{
    var clock = Stopwatch.StartNew();
    while (!File.Exists(path) && clock.Elapsed < TimeSpan.FromSeconds(10))
        Thread.Sleep(10);
    if (!File.Exists(path))
        throw new InvalidOperationException("The owned child did not publish its PID.");
}

static int RunFormatterCaptureProbe(string assemblyPath, string root, bool failRelease)
{
    // This synchronous entry thread supervises independently of the constrained pool.
    if (Thread.CurrentThread.IsThreadPoolThread)
        throw new InvalidOperationException("Probe supervision must not consume a worker.");
    var captureType = Assembly.LoadFrom(assemblyPath).GetType(
        "RoslynMcp.Tests.FormatterBaselineContractTests+IncrementalTextCapture", throwOnError: true)!;
    var constructor = captureType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null, [typeof(StreamReader)], modifiers: null)!;
    var completionProperty = captureType.GetProperty("Completion", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var snapshotProperty = captureType.GetProperty("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var releasePath = Path.Combine(root, "formatter-release");
    var pidPath = Path.Combine(root, "formatter.pid");
    var startInfo = new ProcessStartInfo("dotnet")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = new UTF8Encoding(false),
        StandardErrorEncoding = new UTF8Encoding(false)
    };
    foreach (var argument in new[] { Assembly.GetExecutingAssembly().Location, "formatter-output", pidPath, releasePath })
        startInfo.ArgumentList.Add(argument);
    using var child = Process.Start(startInfo)!;
    Task<string>? stdout = null;
    Task<string>? stderr = null;
    ThreadPool.GetMinThreads(out var originalMinWorkers, out var originalMinIo);
    ThreadPool.GetMaxThreads(out var originalMaxWorkers, out var originalMaxIo);
    using var sentinel = new ManualResetEventSlim();
    var sentinelQueued = false;
    Exception? primaryFailure = null;
    var cleanupFailures = new List<Exception>();
    try
    {
        WaitForFile(pidPath);
        if (child.HasExited || File.ReadAllText(pidPath) != child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The owned silent writer did not become ready.");
        if (!ThreadPool.SetMinThreads(2, originalMinIo) || !ThreadPool.SetMaxThreads(2, originalMaxIo))
            throw new InvalidOperationException("Cannot establish isolated two-worker capacity.");
        var stdoutCapture = constructor.Invoke([child.StandardOutput]);
        stdout = (Task<string>)completionProperty.GetValue(stdoutCapture)!;
        var stderrCapture = constructor.Invoke([child.StandardError]);
        stderr = (Task<string>)completionProperty.GetValue(stderrCapture)!;
        // Both real CaptureAsync calls reached their first read and published Completion.
        ThreadPool.QueueUserWorkItem(_ => sentinel.Set());
        sentinelQueued = true;
        var clock = Stopwatch.StartNew();
        var zeroWorkerSamples = 0;
        var samples = 0;
        while (!sentinel.IsSet && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (child.HasExited || stdout.IsCompleted || stderr.IsCompleted)
                throw new InvalidOperationException("Silent writer/captures ended before sentinel observation.");
            ThreadPool.GetAvailableThreads(out var available, out _);
            samples++;
            if (available == 0)
                zeroWorkerSamples++;
            Thread.Sleep(10);
        }
        var progressed = sentinel.IsSet;
        var remainedLive = !child.HasExited && !stdout.IsCompleted && !stderr.IsCompleted;
        Console.WriteLine($"formatter-capture: sentinel={progressed}, childLive={!child.HasExited}, stdoutPending={!stdout.IsCompleted}, stderrPending={!stderr.IsCompleted}, zeroWorkerSamples={zeroWorkerSamples}/{samples}, elapsedMs={clock.ElapsedMilliseconds}");
        if (failRelease)
        {
            // A real filesystem error in finally must not hide the original failure or
            // prevent subsequent attempts to kill/reap the writer and observe both readers.
            Directory.CreateDirectory(releasePath);
            throw new InvalidOperationException("formatter-primary-sentinel");
        }
        File.WriteAllText(releasePath, "release");
        // Restore only this disposable process so OLD readers and queued callbacks can drain.
        ThreadPool.SetMaxThreads(originalMaxWorkers, originalMaxIo);
        ThreadPool.SetMinThreads(originalMinWorkers, originalMinIo);
        var snapshotClock = Stopwatch.StartNew();
        while (((string)snapshotProperty.GetValue(stdoutCapture)! != "o" ||
                (string)snapshotProperty.GetValue(stderrCapture)! != "e") &&
               snapshotClock.Elapsed < TimeSpan.FromSeconds(5))
            Thread.Sleep(10);
        if ((string)snapshotProperty.GetValue(stdoutCapture)! != "o" ||
            (string)snapshotProperty.GetValue(stderrCapture)! != "e" ||
            child.HasExited || stdout.IsCompleted || stderr.IsCompleted)
            throw new InvalidOperationException("Actual formatter snapshots must expose one byte before EOF.");
        File.WriteAllText(releasePath + ".tail", "release");
        if (!child.WaitForExit(10_000) || !Task.WaitAll([stdout, stderr], TimeSpan.FromSeconds(10)))
            throw new InvalidOperationException("Owned formatter output did not drain after release.");
        const string expectedOut = "out:alpha\r\nβ🙂tail\uFFFD";
        const string expectedErr = "err:alpha\r\nβ🙂tail\uFFFD";
        if (child.ExitCode != 0 || stdout.Result != expectedOut || stderr.Result != expectedErr ||
            (string)snapshotProperty.GetValue(stdoutCapture)! != expectedOut ||
            (string)snapshotProperty.GetValue(stderrCapture)! != expectedErr)
            throw new InvalidOperationException("Actual formatter completion/snapshot lost multibyte output.");
        if (!progressed || !remainedLive)
            throw new InvalidOperationException("Actual formatter reads blocked sentinel progress while pipes remained live.");
        Console.WriteLine("formatter-capture: liveOneByteSnapshots=True, exactMultibyteAndDecoderFlush=True, drained=True");
    }
    catch (Exception exception)
    {
        primaryFailure = exception;
    }
    finally
    {
        // Independent attempts preserve the primary failure and never skip owned cleanup.
        AttemptCleanup(() =>
        {
            if (!ThreadPool.SetMaxThreads(originalMaxWorkers, originalMaxIo))
                throw new InvalidOperationException("Could not restore isolated maximum workers.");
        });
        AttemptCleanup(() =>
        {
            if (!ThreadPool.SetMinThreads(originalMinWorkers, originalMinIo))
                throw new InvalidOperationException("Could not restore isolated minimum workers.");
        });
        AttemptCleanup(() => File.WriteAllText(releasePath, "release"));
        AttemptCleanup(() => File.WriteAllText(releasePath + ".tail", "release"));
        AttemptCleanup(() =>
        {
            if (!child.HasExited)
                child.Kill(entireProcessTree: true);
        });
        AttemptCleanup(() =>
        {
            if (!child.WaitForExit(10_000))
                throw new InvalidOperationException("Owned formatter child survived cleanup.");
        });
        foreach (var reader in new[] { stdout, stderr }.OfType<Task>())
        {
            AttemptCleanup(() =>
            {
                if (!reader.Wait(TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("Actual formatter reader survived cleanup.");
            });
        }
        AttemptCleanup(() =>
        {
            if (sentinelQueued && !sentinel.Wait(TimeSpan.FromSeconds(10)))
                throw new InvalidOperationException("Queued sentinel survived cleanup.");
        });
        if (failRelease)
        {
            AttemptCleanup(() => Console.WriteLine(
                $"formatter-cleanup: childExited={child.HasExited}, stdoutCompleted={stdout?.IsCompleted}, stderrCompleted={stderr?.IsCompleted}"));
        }
    }

    if (primaryFailure is not null)
    {
        if (cleanupFailures.Count != 0)
            throw new AggregateException("Formatter probe primary failure and cleanup failures.",
                [primaryFailure, .. cleanupFailures]);
        ExceptionDispatchInfo.Capture(primaryFailure).Throw();
    }
    if (cleanupFailures.Count != 0)
        throw new AggregateException("Owned formatter probe cleanup failed.", cleanupFailures);
    return 0;

    void AttemptCleanup(Action cleanup)
    {
        try { cleanup(); }
        catch (Exception exception) { cleanupFailures.Add(exception); }
    }
}
