namespace RoslynMcp.Tests;

internal static class TestFixtureFileSystem
{
    private static readonly StringComparer PathComponentComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly string[] IgnoredDirectoryNames = ["bin"];

    public static string CreateSampleSolutionCopy(string repositoryRootPath, string sampleSolutionPath)
    {
        var sampleRoot = Path.GetDirectoryName(sampleSolutionPath)
            ?? throw new InvalidOperationException("Sample solution root could not be resolved.");
        var tempRoot = Path.Combine(TestTempRoot.Current, Guid.NewGuid().ToString("N"));
        CopyDirectory(sampleRoot, tempRoot);
        CopyRepositorySupportFiles(repositoryRootPath, tempRoot);

        var slnxPath = Path.Combine(tempRoot, "SampleSolution.slnx");
        if (File.Exists(slnxPath))
        {
            return slnxPath;
        }

        var slnPath = Path.Combine(tempRoot, "SampleSolution.sln");
        if (File.Exists(slnPath))
        {
            return slnPath;
        }

        throw new InvalidOperationException("Copied sample solution is missing a solution file.");
    }

    public static void DeleteDirectoryIfExists(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        // `Directory.Delete(..., recursive: true)` fails on Windows when the tree contains
        // read-only files (e.g. `.git/objects/**` loose objects after `git init`, which the
        // git tooling marks read-only by convention). Clear the read-only attribute on every
        // file first so the recursive delete succeeds. Equivalent to PowerShell's
        // `Remove-Item -Recurse -Force`. Tests that run `git init` inside an
        // `IsolatedWorkspaceScope` rely on this behavior at scope-dispose time.
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                ClearReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts &&
                                       (ex is IOException || ex is UnauthorizedAccessException))
            {
                Thread.Sleep(100 * attempt);
            }
        }
    }

    public static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or PlatformNotSupportedException)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process is null || !process.WaitForExit(milliseconds: 5_000))
        {
            process?.Kill(entireProcessTree: true);
            return false;
        }

        return process.ExitCode == 0 && Directory.Exists(linkPath);
    }

    public static bool TryCreateDirectorySymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or PlatformNotSupportedException)
        {
            return false;
        }
    }

    public static bool TryCreateFileLink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static void ClearReadOnlyAttributes(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }
    }

    public static string FindFixturePath(string repositoryRootPath, string fixtureDirectory, params string[] candidateFiles)
    {
        var dir = repositoryRootPath;
        while (dir is not null)
        {
            foreach (var candidateFile in candidateFiles)
            {
                var candidate = Path.Combine(dir, "samples", fixtureDirectory, candidateFile);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(
            $"Could not find fixture '{fixtureDirectory}'. Ensure the samples directory exists at the repo root.");
    }

    public static string FindRepositoryRoot()
    {
        var sourceDirectory = Path.GetDirectoryName(GetSourceFilePath());
        foreach (var startDirectory in new[]
                 {
                     AppContext.BaseDirectory,
                     Environment.CurrentDirectory,
                     sourceDirectory,
                 })
        {
            if (string.IsNullOrEmpty(startDirectory))
            {
                continue;
            }

            var dir = startDirectory;
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir, "RoslynMcp.slnx")) &&
                    File.Exists(Path.Combine(dir, "Directory.Build.props")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }

    private static string GetSourceFilePath(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "") =>
        sourceFilePath;

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            if (ShouldSkipFile(file))
            {
                continue;
            }

            var destinationFile = Path.Combine(destinationDir, Path.GetFileName(file));
            CopyFileWithRetry(file, destinationFile);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDir))
        {
            if (ShouldSkipDirectory(directory))
            {
                continue;
            }

            var destinationSubdirectory = Path.Combine(destinationDir, Path.GetFileName(directory));
            CopyDirectory(directory, destinationSubdirectory);
        }
    }

    // verify-release.ps1 runs `dotnet restore` on the sample solution before parallel tests start.
    // MSBuild / NuGet may still be finalizing transient artifacts (obj/**/*.assets.cache,
    // CoreCompileInputs.cache) when the first parallel tests race to copy the fixture, producing
    // IOExceptions with HRESULT 0x80070020 (ERROR_SHARING_VIOLATION). Retrying with backoff lets
    // the host close the handle without falsely failing the test on a copy-time race.
    private static void CopyFileWithRetry(string sourceFile, string destinationFile)
    {
        const int maxAttempts = 5;
        var delayMs = 50;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(sourceFile, destinationFile, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(delayMs);
                delayMs *= 2;
            }
            catch (IOException) when (CanIgnoreMissingTransientFile(sourceFile))
            {
                return;
            }
        }
    }

    private static void CopyRepositorySupportFiles(string repositoryRootPath, string destinationRoot)
    {
        foreach (var fileName in new[] { "Directory.Build.props", "Directory.Packages.props", "global.json", "BannedSymbols.txt" })
        {
            var sourcePath = Path.Combine(repositoryRootPath, fileName);
            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, Path.Combine(destinationRoot, fileName), overwrite: true);
            }
        }
    }

    private static bool ShouldSkipDirectory(string path)
    {
        var name = Path.GetFileName(path);
        for (var i = 0; i < IgnoredDirectoryNames.Length; i++)
        {
            if (PathComponentComparer.Equals(name, IgnoredDirectoryNames[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldSkipFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith("~", StringComparison.Ordinal) ||
               name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanIgnoreMissingTransientFile(string path)
    {
        return !File.Exists(path) && ShouldSkipFile(path);
    }
}
