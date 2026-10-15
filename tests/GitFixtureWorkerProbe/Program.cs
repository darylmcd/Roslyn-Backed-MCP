using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;

if (args[0] == "child")
{
    File.WriteAllText(args[2], "entered");
    var startup = Stopwatch.StartNew();
    while (!File.Exists(args[1]) && startup.Elapsed < TimeSpan.FromSeconds(15))
        Thread.Sleep(10);
    return File.Exists(args[1]) ? 0 : 3;
}

var assemblyPath = Path.GetFullPath(args[0]);
var root = Path.GetFullPath(args[1]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(Path.GetDirectoryName(assemblyPath)!, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
var runner = Assembly.LoadFrom(assemblyPath).GetType("RoslynMcp.Tests.Support.GitFixtureRunner", throwOnError: true)!;
var method = runner.GetMethod("RunGitCaptureAsync", BindingFlags.Public | BindingFlags.Static)
    ?? runner.GetMethod("RunGitCapture", BindingFlags.Public | BindingFlags.Static)!;
var entered = Path.Combine(root, "entered");
var release = Path.Combine(root, "release");
var trace = Path.Combine(root, "git-trace.jsonl");
Environment.SetEnvironmentVariable("GIT_TRACE2_EVENT", trace);
static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
var alias = "!dotnet " + Quote(Assembly.GetExecutingAssembly().Location) + " child " + Quote(release) + " " + Quote(entered);
ThreadPool.SetMinThreads(1, 1);
if (!ThreadPool.SetMaxThreads(1, 1))
    throw new InvalidOperationException("Cannot establish the isolated one-worker context.");
Task<string>? task = null;
try
{
    task = Task.Run(async () =>
    {
        var result = method.Invoke(null, [root, new[] { "-c", "alias.probe=" + alias, "probe" }]);
        return result is Task<string> asynchronous ? await asynchronous : (string)result!;
    });
    var startup = Stopwatch.StartNew();
    while (!File.Exists(entered) && startup.Elapsed < TimeSpan.FromSeconds(10))
        Thread.Sleep(10);
    if (!File.Exists(entered))
        throw new InvalidOperationException("Owned Git child never entered.");
    File.WriteAllText(release, "release");
    var completed = task.Wait(TimeSpan.FromSeconds(4));
    if (!completed)
    {
        var exits = File.Exists(trace)
            ? File.ReadLines(trace).Count(line => line.Contains("\"event\":\"exit\"", StringComparison.Ordinal))
            : 0;
        throw new InvalidOperationException($"Git fixture blocked its only worker after native exit: exits={exits}, pending={ThreadPool.PendingWorkItemCount}.");
    }
    return 0;
}
finally
{
    File.WriteAllText(release, "release");
    ThreadPool.SetMaxThreads(8, 8);
    // The owned child has a deadline and the process runner owns bounded termination/drain.
    if (task is not null)
        task.GetAwaiter().GetResult();
}
