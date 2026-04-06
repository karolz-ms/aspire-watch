namespace AspireWatchDemo.WatchBootstrap;

public static class WorkspaceLocator
{
    public static string FindRepositoryRoot(string? startDirectory = null)
    {
        var current = new DirectoryInfo(startDirectory ?? Directory.GetCurrentDirectory());

        while (current is not null)
        {
            var hasGlobalJson = File.Exists(Path.Combine(current.FullName, "global.json"));
            var hasGitDirectory = Directory.Exists(Path.Combine(current.FullName, ".git"));
            if (hasGlobalJson || hasGitDirectory)
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root. Expected to find 'global.json' or '.git'.");
    }
}
