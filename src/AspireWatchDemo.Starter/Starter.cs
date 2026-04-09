using System.IO.Pipes;
using System.Text;
using AspireWatchDemo.WatchBootstrap;

const string AppHostLogPipeEnvironmentVariable = "ASPIRE_STARTER_LOG_PIPE";
const string AppHostLogLevelEnvironmentVariable = "ASPIRE_STARTER_LOG_LEVEL";

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

var dotnet = DotnetSdkLocator.Resolve();

Console.WriteLine($"[starter] Repo root: {repoRoot}");
Console.WriteLine($"[starter] dotnet: {dotnet.DotnetExecutablePath}");
Console.WriteLine($"[starter] SDK dir: {dotnet.SdkDirectory}");
Console.WriteLine($"[starter] Restoring '{restoreTargetPath}' to fetch Watch.Aspire and the demo services...");

var restoreExitCode = await ProcessRunner.RunStreamingAsync(
    dotnet.DotnetExecutablePath,
    ["restore", restoreTargetPath],
    repoRoot,
    cancellationToken: cancellationSource.Token);

if (restoreExitCode != 0)
{
    Console.Error.WriteLine($"[starter] 'dotnet restore' failed with exit code {restoreExitCode}.");
    return restoreExitCode;
}

var watch = WatchAspireLocator.Resolve(dotnet, appHostProjectPath);
var appHostLogPipeName = PipeNameFactory.CreateName("apphost-log");
var appHostLogTask = RelayAppHostLogsAsync(appHostLogPipeName, cancellationSource.Token);
var hostEnvironmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    [AppHostLogPipeEnvironmentVariable] = appHostLogPipeName,
    [AppHostLogLevelEnvironmentVariable] = "Debug"
};

Console.WriteLine($"[starter] Watch.Aspire package version: {watch.PackageVersion}");
Console.WriteLine($"[starter] Watch.Aspire entrypoint: {watch.WatchDllPath}");
Console.WriteLine($"[starter] AppHost log pipe: {appHostLogPipeName}");

var hostArguments = WatchAspireCommandBuilder.BuildHostArguments(watch, appHostProjectPath, args);
var appHostWorkingDirectory = Path.GetDirectoryName(appHostProjectPath)!;

Console.WriteLine($"[starter] Launching Watch.Aspire against '{appHostProjectPath}'.");
Console.WriteLine($"[starter] Working directory: {appHostWorkingDirectory}");

var exitCode = await ProcessRunner.RunStreamingAsync(
    watch.Dotnet.DotnetExecutablePath,
    hostArguments,
    appHostWorkingDirectory,
    environmentVariables: hostEnvironmentVariables,
    cancellationToken: cancellationSource.Token);

if (!cancellationSource.IsCancellationRequested)
{
    cancellationSource.Cancel();
}

try
{
    await appHostLogTask;
}
catch (OperationCanceledException)
{
    // Expected during shutdown.
}

return exitCode;

static async Task RelayAppHostLogsAsync(string pipeName, CancellationToken cancellationToken)
{
    try
    {
        await using var logPipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        Console.WriteLine($"[starter] Waiting for AppHost log relay on pipe '{pipeName}'.");
        await logPipe.WaitForConnectionAsync(cancellationToken);
        Console.WriteLine("[starter] AppHost log relay connected.");

        using var reader = new StreamReader(logPipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                Console.WriteLine($"[apphost-log] {line}");
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Expected during shutdown.
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine($"[starter] AppHost log relay failed: {ex.Message}");
    }
}
