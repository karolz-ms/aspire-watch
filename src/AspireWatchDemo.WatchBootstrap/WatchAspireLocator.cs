namespace AspireWatchDemo.WatchBootstrap;

public static class WatchAspireLocator
{
    public const string RequestedPackageVersion = "10.0.201";

    private const string PackageId = "microsoft.dotnet.hotreload.watch.aspire";
    private const string EntryPointFileName = "Microsoft.DotNet.HotReload.Watch.Aspire.dll";

    public static WatchAspireLocation Resolve(string? preferredVersion = RequestedPackageVersion)
    {
        var dotnet = DotnetSdkLocator.Resolve();
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
