namespace AspireWatchDemo.WatchBootstrap;

public static class WatchAspireCommandBuilder
{

    public static IReadOnlyList<string> BuildHostArguments(
        WatchAspireLocation location,
        string projectPath,
        IEnumerable<string>? applicationArguments = null)
    {
        var args = new List<string> { location.WatchDllPath };

        args.AddRange(["host", "--sdk", location.Dotnet.SdkDirectory, "--entrypoint", projectPath, "--verbose"]);

        if (applicationArguments is not null)
        {
            args.AddRange(applicationArguments);
        }

        return args;
    }

    public static IReadOnlyList<string> BuildServerArguments(
        WatchAspireLocation location,
        WatchPipeNames pipes,
        IEnumerable<string> resourcePaths)
    {
        var args = new List<string>
        {
            location.WatchDllPath,
            "server",
            "--sdk", location.Dotnet.SdkDirectory,
            "--server", pipes.ServerPipeName,
            "--status-pipe", pipes.StatusPipeName,
            "--control-pipe", pipes.ControlPipeName,
            "--verbose"
        };

        foreach (var resourcePath in resourcePaths)
        {
            args.Add("--resource");
            args.Add(resourcePath);
        }

        return args;
    }

    public static IReadOnlyList<string> BuildResourceArguments(
        WatchAspireLocation location,
        string serverPipeName,
        string projectPath,
        IReadOnlyDictionary<string, string> environmentVariables)
    {
        var args = new List<string>
        {
            location.WatchDllPath,
            "resource",
            "--server", serverPipeName,
            "--entrypoint", projectPath,
            "--no-launch-profile",
            "--verbose"
        };

        foreach (var pair in environmentVariables)
        {
            args.Add("-e");
            args.Add($"{pair.Key}={pair.Value}");
        }

        return args;
    }
}
