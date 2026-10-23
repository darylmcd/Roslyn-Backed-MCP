using System.Xml.Linq;

namespace RoslynMcp.Tests;

internal static class TestFixtureFileSystem
{
    private static readonly StringComparer PathComponentComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly string[] IgnoredDirectoryNames = ["bin"];

    private enum SampleCopyKind { Solution, Library, LibraryTests }

    public static string CreateSampleSolutionCopy(string repositoryRootPath, string sampleSolutionPath) =>
        CreateSampleCopy(repositoryRootPath, sampleSolutionPath, SampleCopyKind.Solution).EntryPath;

    public static (string RootPath, string EntryPath) CreateSampleLibraryCopy(
        string repositoryRootPath, string sampleSolutionPath) =>
        CreateSampleCopy(repositoryRootPath, sampleSolutionPath, SampleCopyKind.Library);

    public static (string RootPath, string EntryPath) CreateSampleLibraryTestsCopy(
        string repositoryRootPath, string sampleSolutionPath) =>
        CreateSampleCopy(repositoryRootPath, sampleSolutionPath, SampleCopyKind.LibraryTests);

    private static (string RootPath, string EntryPath) CreateSampleCopy(
        string repositoryRootPath, string sampleSolutionPath, SampleCopyKind kind)
    {
        var sampleRoot = Path.GetDirectoryName(sampleSolutionPath)
            ?? throw new InvalidOperationException("Sample solution root could not be resolved.");
        var tempRoot = Path.Combine(TestTempRoot.Current, Guid.NewGuid().ToString("N"));
        try
        {
            // Preserve sources, restored references and repository compiler settings.
            // The two-project solution retains the common watched root for both projects.
            string[] projects = kind switch
            {
                SampleCopyKind.Library => ["SampleLib"],
                SampleCopyKind.LibraryTests => ["SampleLib", "SampleLib.Tests"],
                _ => [],
            };
            if (kind == SampleCopyKind.Solution)
                CopyDirectory(sampleRoot, tempRoot);
            else
                foreach (var project in projects)
                    CopyDirectory(Path.Combine(sampleRoot, project), Path.Combine(tempRoot, project));
            CopyRepositorySupportFiles(repositoryRootPath, tempRoot);

            if (kind == SampleCopyKind.LibraryTests)
            {
                foreach (var project in projects)
                    if (!File.Exists(Path.Combine(tempRoot, project, project + ".csproj")))
                        throw new InvalidOperationException("Copied sample fixture is missing a required project file.");
                new XElement("Solution", projects.Select(project =>
                    new XElement("Project", new XAttribute("Path", $"{project}/{project}.csproj"))))
                    .Save(Path.Combine(tempRoot, "LibraryTests.slnx"));
            }

            string[] candidates = kind switch
            {
                SampleCopyKind.Library => [Path.Combine("SampleLib", "SampleLib.csproj")],
                SampleCopyKind.LibraryTests => ["LibraryTests.slnx"],
                _ => ["SampleSolution.slnx", "SampleSolution.sln"],
            };
            foreach (var candidate in candidates)
            {
                var path = Path.Combine(tempRoot, candidate);
                if (File.Exists(path))
                    return (tempRoot, path);
            }
            throw new InvalidOperationException("Copied sample fixture is missing its project or solution file.");
        }
        catch (Exception initializationFailure)
        {
            try
            {
                DeleteDirectoryIfExists(tempRoot);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Fixture copy and cleanup both failed.", initializationFailure, cleanupFailure);
            }
            throw;
        }
    }

    public static void DeleteDirectoryIfExists(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // Keep literal child names returned by enumeration in the same
            // extended namespace when clearing attributes and deleting them.
            path = Path.GetFullPath(path);
            if (!path.StartsWith(@"\\?\", StringComparison.Ordinal) && !path.StartsWith(@"\\.\", StringComparison.Ordinal))
                path = path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
        }
        // Git's owned loose objects can be read-only. Clear only regular owned files;
        // neither attribute traversal nor Directory.Delete may follow fixture links.
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(path);
                }
                catch (FileNotFoundException) { return; }
                catch (DirectoryNotFoundException) { return; }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(path, recursive: false);
                    else File.Delete(path);
                    return;
                }
                if ((attributes & FileAttributes.Directory) == 0) return;
                ClearReadOnlyAttributes(path);
                // The platform recursive delete removes nested reparse points themselves.
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

        // Symbolic links need Developer Mode or elevation; a junction needs neither. Created
        // in-process: a process launch here is a load-sensitive failure that callers turn into a
        // silent Assert.Inconclusive.
        WindowsDirectoryJunction.Create(linkPath, targetPath);
        return true;
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
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
        };
        foreach (var file in Directory.EnumerateFiles(path, "*", options))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) == FileAttributes.ReadOnly)
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
