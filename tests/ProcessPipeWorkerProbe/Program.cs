using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;

if (args[0] is "hold" or "exit" or "large")
{
    File.WriteAllText(args[1] + ".tmp", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    File.Move(args[1] + ".tmp", args[1]);
    if (args[0] == "hold")
    {
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(args[2]) && deadline.Elapsed < TimeSpan.FromSeconds(60))
            Thread.Sleep(10);
        return File.Exists(args[2]) ? 0 : 91;
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
