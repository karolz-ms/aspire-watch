namespace AspireWatchDemo.WatchBootstrap;

public sealed record WatchAspireLocation(DotnetSdkInfo Dotnet, string WatchDllPath, string PackageVersion);
