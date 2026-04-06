using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace AspireWatchDemo.WatchBootstrap;

public static class DotnetSdkLocator
{
    // Reference: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables#net-sdk-and-cli-environment-variables
    
    public static DotnetSdkInfo Resolve()
    {
        var dotnetExecutable = ResolveDotnetExecutable();
        var infoOutput = RunProcessCapture(dotnetExecutable, "--info");

        var sdkDirectory = TryReadDotnetInfoValue(infoOutput, "Base Path")
            ?? throw new InvalidOperationException("Could not determine the active .NET SDK directory from 'dotnet --info'.");

        var sdkVersion = TryReadDotnetInfoValue(infoOutput, "Version") ?? "unknown";

        return new(
            DotnetExecutablePath: dotnetExecutable,
            SdkDirectory: sdkDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            SdkVersion: sdkVersion);
    }

    private static string? TryReadDotnetInfoValue(string output, string label)
    {
        var match = Regex.Match(output, $@"^\s*{Regex.Escape(label)}:\s*(.+)$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string ResolveDotnetExecutable()
    {
        foreach (var candidate in GetDotnetExecutableCandidates())
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "dotnet";
    }

    private static IEnumerable<string?> GetDotnetExecutableCandidates()
    {
        // Follow the documented lookup order for DOTNET_HOST_PATH and DOTNET_ROOT* variables,
        // then fall back to the common default installation locations for the current platform.
        yield return Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");

        foreach (var root in GetConfiguredDotnetRoots())
        {
            yield return Path.Combine(root, GetDotnetExecutableFileName());
        }

        foreach (var path in GetDefaultDotnetExecutablePaths())
        {
            yield return path;
        }
    }

    private static IEnumerable<string> GetConfiguredDotnetRoots()
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);

        foreach (var variableName in GetDotnetRootVariableNames())
        {
            var value = Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> GetDotnetRootVariableNames()
    {
        var architectureSpecific = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm => "DOTNET_ROOT_ARM",
            Architecture.Arm64 => "DOTNET_ROOT_ARM64",
            Architecture.X86 => "DOTNET_ROOT_X86",
            Architecture.X64 => "DOTNET_ROOT_X64",
            _ => null
        };

        if (architectureSpecific is not null)
        {
            yield return architectureSpecific;
        }

        if (OperatingSystem.IsWindows() && Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
        {
            yield return "DOTNET_ROOT(x86)";
        }

        yield return "DOTNET_ROOT";
    }

    private static IEnumerable<string> GetDefaultDotnetExecutablePaths()
    {
        var fileName = GetDotnetExecutableFileName();

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            if (!string.IsNullOrWhiteSpace(programFiles)
                && RuntimeInformation.OSArchitecture == Architecture.Arm64
                && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                yield return Path.Combine(programFiles, "dotnet", "x64", fileName);
            }

            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess && !string.IsNullOrWhiteSpace(programFilesX86))
            {
                yield return Path.Combine(programFilesX86, "dotnet", fileName);
            }

            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, "dotnet", fileName);
            }

            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            if (RuntimeInformation.OSArchitecture == Architecture.Arm64 && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                yield return Path.Combine("/usr/local/share/dotnet", "x64", fileName);
            }

            yield return Path.Combine("/usr/local/share/dotnet", fileName);
            yield return "/usr/local/bin/dotnet";
            yield break;
        }

        if (OperatingSystem.IsLinux())
        {
            yield return Path.Combine("/usr/share/dotnet", fileName);
            yield return Path.Combine("/usr/lib/dotnet", fileName);
            yield return Path.Combine("/usr/local/share/dotnet", fileName);
        }
    }

    private static string GetDotnetExecutableFileName()
        => OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

    private static string RunProcessCapture(string fileName, params string[] arguments)
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
}
