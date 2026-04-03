using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AspireWatchDemo.WatchBootstrap;

public sealed record DotnetSdkInfo(string DotnetExecutablePath, string SdkDirectory, string SdkVersion);

public sealed record WatchAspireLocation(DotnetSdkInfo Dotnet, string WatchDllPath, string PackageVersion);

public sealed record WatchPipeNames(string ServerPipeName, string StatusPipeName, string ControlPipeName);

public enum WatchAspireToolMode
{
    LegacyProjectOption,
    LauncherCommands
}

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

public static class PipeNameFactory
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    public static WatchPipeNames CreateSet(int suffixLength = 6)
    {
        var suffix = CreateRandomSuffix(suffixLength);
        return new(
            ServerPipeName: $"server-{suffix}",
            StatusPipeName: $"status-{suffix}",
            ControlPipeName: $"control-{suffix}");
    }

    public static string CreateRandomSuffix(int length = 6)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        Span<char> buffer = stackalloc char[length];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(buffer);
    }
}

public static class DotnetSdkLocator
{
    public static DotnetSdkInfo Resolve()
    {
        var dotnetExecutable = ResolveDotnetExecutable();
        var infoOutput = RunProcessCapture(dotnetExecutable, "--info");

        var sdkDirectory = TryReadValue(infoOutput, "Base Path")
            ?? throw new InvalidOperationException("Could not determine the active .NET SDK directory from 'dotnet --info'.");

        var versionMatch = Regex.Match(infoOutput, @"^\s*Version:\s*(.+)$", RegexOptions.Multiline);
        var sdkVersion = versionMatch.Success ? versionMatch.Groups[1].Value.Trim() : "unknown";

        return new(
            DotnetExecutablePath: dotnetExecutable,
            SdkDirectory: sdkDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            SdkVersion: sdkVersion);
    }

    public static string ResolveDotnetExecutable()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
            Environment.GetEnvironmentVariable("DOTNET_EXE"),
            Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } dotnetRoot
                ? Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet")
                : null,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"),
        };

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            ?? "dotnet";
    }

    internal static string RunProcessCapture(string fileName, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start '{fileName}'.");
        }

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'{fileName} {string.Join(' ', arguments)}' exited with code {process.ExitCode}.{Environment.NewLine}{standardError}");
        }

        return string.IsNullOrWhiteSpace(standardOutput) ? standardError : standardOutput;
    }

    private static string? TryReadValue(string output, string label)
    {
        var match = Regex.Match(output, $@"^\s*{Regex.Escape(label)}:\s*(.+)$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}

public static class WatchAspireLocator
{
    public const string RequestedPackageVersion = "10.0.200";

    private const string PackageId = "microsoft.dotnet.hotreload.watch.aspire";
    private const string EntryPointFileName = "Microsoft.DotNet.HotReload.Watch.Aspire.dll";

    public static WatchAspireLocation Resolve(string? preferredVersion = RequestedPackageVersion)
    {
        var dotnet = DotnetSdkLocator.Resolve();

        var envOverride = Environment.GetEnvironmentVariable("ASPIRE_WATCH_ASPIRE_DLL");
        if (!string.IsNullOrWhiteSpace(envOverride) && File.Exists(envOverride))
        {
            return new(dotnet, Path.GetFullPath(envOverride), preferredVersion ?? RequestedPackageVersion);
        }

        var globalPackagesFolder = ResolveGlobalPackagesFolder();
        var packageRoot = Path.Combine(globalPackagesFolder, PackageId);

        if (!Directory.Exists(packageRoot))
        {
            throw new DirectoryNotFoundException(
                $"The Watch.Aspire package was not found under '{packageRoot}'. Run 'dotnet restore' for the AppHost project first.");
        }

        foreach (var version in GetCandidateVersions(packageRoot, preferredVersion))
        {
            var versionRoot = Path.Combine(packageRoot, version);
            if (!Directory.Exists(versionRoot))
            {
                continue;
            }

            var directPath = Path.Combine(versionRoot, "tools", "net10.0", "any", EntryPointFileName);
            if (File.Exists(directPath))
            {
                return new(dotnet, directPath, version);
            }

            var fallback = Directory.EnumerateFiles(
                versionRoot,
                EntryPointFileName,
                SearchOption.AllDirectories).FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return new(dotnet, fallback, version);
            }
        }

        throw new FileNotFoundException(
            $"Could not locate '{EntryPointFileName}' beneath '{packageRoot}'. Ensure 'Microsoft.DotNet.HotReload.Watch.Aspire' was restored.");
    }

    public static string ResolveGlobalPackagesFolder()
    {
        var configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, ".nuget", "packages");
    }

    private static IEnumerable<string> GetCandidateVersions(string packageRoot, string? preferredVersion)
    {
        var versions = Directory.EnumerateDirectories(packageRoot)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderByDescending(ParseVersionSafe)
            .ToList();

        if (!string.IsNullOrWhiteSpace(preferredVersion))
        {
            yield return preferredVersion;
        }

        foreach (var version in versions.Where(version => !string.Equals(version, preferredVersion, StringComparison.OrdinalIgnoreCase)))
        {
            yield return version;
        }
    }

    private static Version ParseVersionSafe(string version)
        => Version.TryParse(version, out var parsed) ? parsed : new Version(0, 0);
}

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
            throw new NotSupportedException("The public 10.0.200 Watch.Aspire package does not expose the separate 'server' launcher yet.");
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
            throw new NotSupportedException("The public 10.0.200 Watch.Aspire package does not expose the separate 'resource' launcher yet.");
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

public static class ProcessRunner
{
    public static async Task<int> RunStreamingAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
            EnableRaisingEvents = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.OutputDataReceived += static (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                Console.Out.WriteLine(e.Data);
            }
        };

        process.ErrorDataReceived += static (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                Console.Error.WriteLine(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start '{fileName}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
