using AspireWatchDemo.WatchBootstrap;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

var watchOptions = WatchAspireOptions.FromArguments(args);
if (watchOptions.ShouldWaitForDebugger(WatchAspireOptions.AppHostMoniker))
{
    Console.WriteLine("[apphost] Waiting for debugger to attach...");
    WatchAspireOptions.WaitForDebugger(cancellationSource.Token);
}

EnsureEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:18888");
EnsureEnvironment("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL", "http://127.0.0.1:18889");
EnsureEnvironment("DOTNET_DASHBOARD_OTLP_HTTP_ENDPOINT_URL", "http://127.0.0.1:18890");
EnsureEnvironment("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", Environment.GetEnvironmentVariable("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL") ?? "http://127.0.0.1:18889");
EnsureEnvironment("ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL", Environment.GetEnvironmentVariable("DOTNET_DASHBOARD_OTLP_HTTP_ENDPOINT_URL") ?? "http://127.0.0.1:18890");
EnsureEnvironment("DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", "true");
EnsureEnvironment("ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", "true");
EnsureEnvironment("ASPIRE_ALLOW_UNSECURED_TRANSPORT", "true");

var forwardedArgs = WatchAspireOptions.FilterApplicationArguments(args);
var builder = DistributedApplication.CreateBuilder(forwardedArgs);

var repoRoot = WorkspaceLocator.FindRepositoryRoot(Directory.GetCurrentDirectory());
var appHostProjectPath = Path.Combine(repoRoot, "src", "AspireWatchDemo.AppHost", "AspireWatchDemo.AppHost.csproj");
var apiProjectPath = Path.Combine(repoRoot, "src", "AspireWatchDemo.ApiService", "AspireWatchDemo.ApiService.csproj");
var webProjectPath = Path.Combine(repoRoot, "src", "AspireWatchDemo.Web", "AspireWatchDemo.Web.csproj");

ValidateProjectPath(appHostProjectPath, "apphost");
ValidateProjectPath(apiProjectPath, "api");
ValidateProjectPath(webProjectPath, "web");

var dotnet = DotnetSdkLocator.Resolve();
var watch = WatchAspireLocator.Resolve(dotnet, watchOptions, appHostProjectPath);
var pipes = PipeNameFactory.CreateSet();

Console.WriteLine($"[apphost] Repo root: {repoRoot}");
Console.WriteLine($"[apphost] Watch.Aspire source: {watch.SourceDescription}");
Console.WriteLine($"[apphost] Watch.Aspire launch target: {watch.LaunchTargetPath}");
Console.WriteLine($"[apphost] SDK directory: {watch.Dotnet.SdkDirectory}");
Console.WriteLine($"[apphost] Pipe names: server={pipes.ServerPipeName}, status={pipes.StatusPipeName}, control={pipes.ControlPipeName}");

const int apiPort = 5071;
const int webPort = 5072;

IResourceBuilder<ExecutableResource>? watchServer = null;

 builder.Services.AddSingleton(pipes);
builder.Services.AddHostedService<WatchPipeMonitorHostedService>();

watchServer = builder.AddExecutable(
        "watch-server",
        watch.Dotnet.DotnetExecutablePath,
        ".",
        [.. WatchAspireCommandBuilder.BuildServerArguments(watch, pipes, [apiProjectPath, webProjectPath])])
    .WithEnvironment("ASPIRE_WATCH_PIPE_CONNECTION_TIMEOUT_SECONDS", "30");

var api = AddWatchedService("api-service", apiProjectPath, apiPort);
if (watchServer is not null)
{
    api = api.WaitForStart(watchServer);
}

var web = AddWatchedService("web-service", webProjectPath, webPort).WaitFor(api);
if (watchServer is not null)
{
    web = web.WaitForStart(watchServer);
}

builder.Build().Run();

IResourceBuilder<ExecutableResource> AddWatchedService(string name, string projectPath, int port)
{
    var url = $"http://127.0.0.1:{port}";
    var environmentVariables = new Dictionary<string, string>
    {
        ["ASPNETCORE_URLS"] = url,
        ["ASPNETCORE_ENVIRONMENT"] = "Development"
    };

    var arguments = WatchAspireCommandBuilder.BuildResourceArguments(watch, pipes.ServerPipeName, projectPath, environmentVariables);

    var serviceWorkingDirectory = Path.GetDirectoryName(projectPath)!;

    return builder.AddExecutable(
            name,
            watch.Dotnet.DotnetExecutablePath,
            serviceWorkingDirectory,
            [.. arguments])
        .WithEnvironment("ASPNETCORE_URLS", url)
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
        .WithHttpEndpoint(name: "http", port: port, targetPort: port, isProxied: false)
        .WithExternalHttpEndpoints()
        .WithHttpHealthCheck("/health");
}

static void ValidateProjectPath(string projectPath, string name)
{
    if (!File.Exists(projectPath))
    {
        throw new FileNotFoundException($"Could not resolve the {name} project at '{projectPath}'.", projectPath);
    }
}

static void EnsureEnvironment(string name, string value)
{
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
    {
        Environment.SetEnvironmentVariable(name, value);
    }
}
