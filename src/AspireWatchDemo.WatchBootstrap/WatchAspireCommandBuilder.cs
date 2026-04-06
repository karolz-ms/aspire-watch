namespace AspireWatchDemo.WatchBootstrap;

public static class WatchAspireCommandBuilder
{
    public static WatchAspireToolMode GetMode(WatchAspireLocation location)
        => Version.TryParse(location.PackageVersion, out var version) && version.Build < 300
            ? WatchAspireToolMode.LegacyProjectOption
            : WatchAspireToolMode.LauncherCommands;

    public static IReadOnlyList<string> BuildHostArguments(
        WatchAspireLocation location,
        string projectPath,
        IEnumerable<string>? applicationArguments = null)
    {
        var args = new List<string> { location.WatchDllPath };

        if (GetMode(location) == WatchAspireToolMode.LauncherCommands)
        {
            args.AddRange(["host", "--sdk", location.Dotnet.SdkDirectory, "--entrypoint", projectPath, "--verbose"]);
        }
        else
        {
            args.AddRange(["--sdk", location.Dotnet.SdkDirectory, "--project", projectPath, "--verbose"]);
        }

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
        if (GetMode(location) == WatchAspireToolMode.LegacyProjectOption)
        {
            throw new NotSupportedException("The public 10.0.201 Watch.Aspire package does not expose the separate 'server' launcher yet.");
        }

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
        if (GetMode(location) == WatchAspireToolMode.LegacyProjectOption)
        {
            throw new NotSupportedException("The public 10.0.201 Watch.Aspire package does not expose the separate 'resource' launcher yet.");
        }

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

    public static IReadOnlyList<string> BuildProjectWatchArguments(
        WatchAspireLocation location,
        string projectPath,
        bool noLaunchProfile = true,
        IEnumerable<string>? applicationArguments = null)
    {
        var args = new List<string>
        {
            location.WatchDllPath,
            "--sdk", location.Dotnet.SdkDirectory,
            "--project", projectPath,
            "--verbose"
        };

        if (noLaunchProfile)
        {
            args.Add("--no-launch-profile");
        }

        if (applicationArguments is not null)
        {
            args.AddRange(applicationArguments);
        }

        return args;
    }
}
