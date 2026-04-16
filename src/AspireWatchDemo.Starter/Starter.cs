using AspireWatchDemo.WatchBootstrap;

var repoRoot = WorkspaceLocator.FindRepositoryRoot(Directory.GetCurrentDirectory());
var solutionPath = Path.Combine(repoRoot, "AspireWatchDemo.slnx");
var appHostProjectPath = Path.Combine(repoRoot, "src", "AspireWatchDemo.AppHost", "AspireWatchDemo.AppHost.csproj");
var restoreTargetPath = File.Exists(solutionPath) ? solutionPath : appHostProjectPath;

if (!File.Exists(appHostProjectPath))
{
    Console.Error.WriteLine($"[starter] Could not find AppHost project at '{appHostProjectPath}'.");
    return 1;
}

using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

var watchOptions = WatchAspireOptions.FromArguments(args);

var dotnet = DotnetSdkLocator.Resolve();

Console.WriteLine($"[starter] Repo root: {repoRoot}");
Console.WriteLine($"[starter] dotnet: {dotnet.DotnetExecutablePath}");
Console.WriteLine($"[starter] SDK dir: {dotnet.SdkDirectory}");
Console.WriteLine($"[starter] Private Watch.Aspire mode: {(watchOptions.UsePrivateBuild ? "enabled" : "disabled")}");
Console.WriteLine($"[starter] Restoring '{restoreTargetPath}' to fetch Watch.Aspire and the demo services...");

var restoreExitCode = await ProcessRunner.RunStreamingAsync(
    dotnet.DotnetExecutablePath,
    ["restore", restoreTargetPath],
    repoRoot,
    cancellationSource.Token);

if (restoreExitCode != 0)
{
    Console.Error.WriteLine($"[starter] 'dotnet restore' failed with exit code {restoreExitCode}.");
    return restoreExitCode;
}

var watch = WatchAspireLocator.Resolve(dotnet, watchOptions, appHostProjectPath);

Console.WriteLine($"[starter] Watch.Aspire source: {watch.SourceDescription}");
Console.WriteLine($"[starter] Watch.Aspire package version: {watch.PackageVersion}");
Console.WriteLine($"[starter] Watch.Aspire launch target: {watch.LaunchTargetPath}");

var hostArguments = WatchAspireCommandBuilder.BuildHostArguments(watch, appHostProjectPath, args);
var appHostWorkingDirectory = Path.GetDirectoryName(appHostProjectPath)!;

Console.WriteLine($"[starter] Launching Watch.Aspire against '{appHostProjectPath}'.");
Console.WriteLine($"[starter] Working directory: {appHostWorkingDirectory}");

return await ProcessRunner.RunStreamingAsync(
    watch.Dotnet.DotnetExecutablePath,
    hostArguments,
    appHostWorkingDirectory,
    cancellationSource.Token);
