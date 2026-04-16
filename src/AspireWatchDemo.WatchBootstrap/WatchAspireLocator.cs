using System.Xml.Linq;

namespace AspireWatchDemo.WatchBootstrap;

public sealed record WatchAspireLocation(
    DotnetSdkInfo Dotnet,
    IReadOnlyList<string> LaunchArguments,
    string LaunchTargetPath,
    string PackageVersion,
    bool IsPrivateBuild)
{
    public string WatchDllPath => LaunchTargetPath;
    public string SourceDescription => IsPrivateBuild ? "private build" : "NuGet package";
}

public static class WatchAspireLocator
{
    private static string PackageId  => PackageName.ToLowerInvariant();
    private const string PackageName = "Microsoft.DotNet.HotReload.Watch.Aspire";
    private const string EntryPointFileName = "Microsoft.DotNet.HotReload.Watch.Aspire.dll";

    public static WatchAspireLocation Resolve(DotnetSdkInfo dotnet, string? appHostProjectPath = null)
        => Resolve(dotnet, WatchAspireOptions.FromArguments(null), appHostProjectPath);

    public static WatchAspireLocation Resolve(DotnetSdkInfo dotnet, WatchAspireOptions options, string? appHostProjectPath = null)
        => options.UsePrivateBuild
            ? ResolvePrivateBuild(dotnet, options.PrivateBuildProjectPath)
            : ResolveNuGetPackage(dotnet, appHostProjectPath);

    private static WatchAspireLocation ResolvePrivateBuild(DotnetSdkInfo dotnet, string? privateBuildProjectPath)
    {
        if (string.IsNullOrWhiteSpace(privateBuildProjectPath))
        {
            throw new InvalidOperationException(
                $"The '{WatchAspireOptions.PrivateBuildFlag}' option requires a path to the private Watch.Aspire project.");
        }

        var normalizedPath = Path.GetFullPath(privateBuildProjectPath);

        if (!File.Exists(normalizedPath))
        {
            throw new FileNotFoundException(
                $"The private Watch.Aspire project was not found at '{normalizedPath}'.",
                normalizedPath);
        }

        return new(
            dotnet,
            ["run", "--no-build", "--project", normalizedPath, "--"],
            normalizedPath,
            "private-build",
            true);
    }

    private static WatchAspireLocation ResolveNuGetPackage(DotnetSdkInfo dotnet, string? appHostProjectPath)
    {
        var preferredVersion = TryResolvePreferredVersionFromProject(appHostProjectPath);
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
                return new(dotnet, [directPath], directPath, version, false);
            }

            var fallback = Directory.EnumerateFiles(
                versionRoot,
                EntryPointFileName,
                SearchOption.AllDirectories).FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return new(dotnet, [fallback], fallback, version, false);
            }
        }

        throw new FileNotFoundException(
            $"Could not locate '{EntryPointFileName}' beneath '{packageRoot}'. Ensure '{PackageName}' was restored.");
    }

    private static string? TryResolvePreferredVersionFromProject(string? appHostProjectPath)
    {
        if (string.IsNullOrWhiteSpace(appHostProjectPath) || !File.Exists(appHostProjectPath))
        {
            return null;
        }

        try
        {
            var project = XDocument.Load(appHostProjectPath);
            var packageElement = project
                .Descendants()
                .FirstOrDefault(static element =>
                    IsPackageDeclaration(element, "PackageDownload") ||
                    IsPackageDeclaration(element, "PackageReference"));

            return NormalizeVersion(packageElement);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
        {
            return null;
        }
    }

    private static bool IsPackageDeclaration(XElement element, string elementName)
        => string.Equals(element.Name.LocalName, elementName, StringComparison.OrdinalIgnoreCase)
            && string.Equals((string?)element.Attribute("Include"), PackageName, StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeVersion(XElement? packageElement)
    {
        var rawVersion = packageElement?.Attribute("Version")?.Value
            ?? packageElement?.Elements()
                .FirstOrDefault(static element => string.Equals(element.Name.LocalName, "Version", StringComparison.OrdinalIgnoreCase))
                ?.Value;

        return NormalizeVersion(rawVersion);
    }

    private static string? NormalizeVersion(string? rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return null;
        }

        var trimmed = rawVersion.Trim();
        if (trimmed.Contains("$(", StringComparison.Ordinal))
        {
            return null;
        }

        var unwrapped = trimmed.Trim('[', ']', '(', ')').Trim();
        if (string.IsNullOrWhiteSpace(unwrapped))
        {
            return null;
        }

        var version = unwrapped
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(version) ? null : version;
    }

    private static string ResolveGlobalPackagesFolder()
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
            .OfType<string>()
            .Select(s => s.Trim())
            .OrderByDescending(ParseVersionSafe)
            .ToList();

        if (!string.IsNullOrWhiteSpace(preferredVersion) && versions.Contains(preferredVersion))
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
