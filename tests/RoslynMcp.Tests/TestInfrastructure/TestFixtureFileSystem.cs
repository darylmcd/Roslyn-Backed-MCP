namespace RoslynMcp.Tests;

internal static class TestFixtureFileSystem
{
    public static string CreateSampleSolutionCopy(string repositoryRootPath, string sampleSolutionPath)
    {
        var sampleRoot = Path.GetDirectoryName(sampleSolutionPath)
            ?? throw new InvalidOperationException("Sample solution root could not be resolved.");
        var tempRoot = Path.Combine(Path.GetTempPath(), "RoslynMcpTests", Guid.NewGuid().ToString("N"));
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
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
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
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "RoslynMcp.slnx")) &&
                File.Exists(Path.Combine(dir, "Directory.Build.props")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destinationFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destinationFile, overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            var destinationSubdirectory = Path.Combine(destinationDir, Path.GetFileName(directory));
            CopyDirectory(directory, destinationSubdirectory);
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
}