using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using AspireWatchDemo.WatchBootstrap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/*
EnsureEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:18888");
EnsureEnvironment("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL", "http://127.0.0.1:18889");
EnsureEnvironment("DOTNET_DASHBOARD_OTLP_HTTP_ENDPOINT_URL", "http://127.0.0.1:18890");
EnsureEnvironment("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", Environment.GetEnvironmentVariable("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL") ?? "http://127.0.0.1:18889");
EnsureEnvironment("ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL", Environment.GetEnvironmentVariable("DOTNET_DASHBOARD_OTLP_HTTP_ENDPOINT_URL") ?? "http://127.0.0.1:18890");
EnsureEnvironment("DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", "true");
EnsureEnvironment("ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", "true");
EnsureEnvironment("ASPIRE_ALLOW_UNSECURED_TRANSPORT", "true");
*/

const string AppHostLogPipeEnvironmentVariable = "ASPIRE_STARTER_LOG_PIPE";
const string AppHostLogLevelEnvironmentVariable = "ASPIRE_STARTER_LOG_LEVEL";

var builder = DistributedApplication.CreateBuilder(args);

var starterLogPipeName = Environment.GetEnvironmentVariable(AppHostLogPipeEnvironmentVariable);
var starterRequestedLogLevel = ResolveLogLevel(Environment.GetEnvironmentVariable(AppHostLogLevelEnvironmentVariable)) ?? LogLevel.Debug;
if (!string.IsNullOrWhiteSpace(starterLogPipeName))
{
    builder.Services.AddLogging(logging =>
    {
        logging.SetMinimumLevel(starterRequestedLogLevel);
        logging.AddProvider(new NamedPipeLoggerProvider(starterLogPipeName));
    });

    Console.WriteLine($"[apphost] Starter log relay enabled on pipe '{starterLogPipeName}' at level {starterRequestedLogLevel}.");
}

var repoRoot = WorkspaceLocator.FindRepositoryRoot(Directory.GetCurrentDirectory());
var appHostProjectPath = Path.Combine(repoRoot, "src", "AspireWatchDemo.AppHost", "AspireWatchDemo.AppHost.csproj");
var apiProjectPath = Path.Combine(repoRoot, "src", "AspireWatchDemo.ApiService", "AspireWatchDemo.ApiService.csproj");
var webProjectPath = Path.Combine(repoRoot, "src", "AspireWatchDemo.Web", "AspireWatchDemo.Web.csproj");

ValidateProjectPath(appHostProjectPath, "apphost");
ValidateProjectPath(apiProjectPath, "api");
ValidateProjectPath(webProjectPath, "web");

var dotnet = DotnetSdkLocator.Resolve();
var watch = WatchAspireLocator.Resolve(dotnet, appHostProjectPath);
var pipes = PipeNameFactory.CreateSet();

Console.WriteLine($"[apphost] Repo root: {repoRoot}");
Console.WriteLine($"[apphost] Watch.Aspire path: {watch.WatchDllPath}");
Console.WriteLine($"[apphost] SDK directory: {watch.Dotnet.SdkDirectory}");
Console.WriteLine($"[apphost] Pipe names: server={pipes.ServerPipeName}, status={pipes.StatusPipeName}, control={pipes.ControlPipeName}");

const int apiPort = 5071;
const int webPort = 5072;

builder.Services.AddSingleton(pipes);
builder.Services.AddHostedService<WatchPipeMonitorHostedService>();

var watchServer = builder.AddExecutable(
        "watch-server",
        watch.Dotnet.DotnetExecutablePath,
        ".",
        [.. WatchAspireCommandBuilder.BuildServerArguments(watch, pipes, [apiProjectPath, webProjectPath])])
    .WithEnvironment("ASPIRE_WATCH_PIPE_CONNECTION_TIMEOUT_SECONDS", "30");

var api = AddWatchedService("api-service", apiProjectPath, apiPort);
api = api.WaitForStart(watchServer);

var web = AddWatchedService("web-service", webProjectPath, webPort).WaitFor(api);
web = web.WaitForStart(watchServer);

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

static LogLevel? ResolveLogLevel(string? value)
    => Enum.TryParse<LogLevel>(value, ignoreCase: true, out var parsed) ? parsed : null;

internal sealed class NamedPipeLoggerProvider : ILoggerProvider
{
    private readonly Channel<string> _messages = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        AllowSynchronousContinuations = false
    });
    private readonly CancellationTokenSource _disposeTokenSource = new();
    private readonly Task _pumpTask;
    private volatile bool _disabled;

    public NamedPipeLoggerProvider(string pipeName)
    {
        _pumpTask = Task.Run(() => PumpAsync(pipeName, _messages.Reader, _disposeTokenSource.Token));
    }

    public ILogger CreateLogger(string categoryName)
        => new NamedPipeLogger(categoryName, _messages.Writer, () => _disabled);

    public void Dispose()
    {
        _disabled = true;
        _messages.Writer.TryComplete();
        _disposeTokenSource.Cancel();

        try
        {
            _pumpTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Normal during shutdown.
        }
        finally
        {
            _disposeTokenSource.Dispose();
        }
    }

    private async Task PumpAsync(string name, ChannelReader<string> reader, CancellationToken cancellationToken)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                name,
                PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            await pipe.ConnectAsync(5000, cancellationToken);

            await using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };

            await foreach (var message in reader.ReadAllAsync(cancellationToken))
            {
                await writer.WriteLineAsync(message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal during shutdown.
        }
        catch (TimeoutException)
        {
            _disabled = true;
            _messages.Writer.TryComplete();
        }
        catch (IOException)
        {
            _disabled = true;
            _messages.Writer.TryComplete();
        }
        catch (UnauthorizedAccessException)
        {
            _disabled = true;
            _messages.Writer.TryComplete();
        }
    }
}

internal sealed class NamedPipeLogger(
    string categoryName,
    ChannelWriter<string> messageWriter,
    Func<bool> isDisabled) : ILogger
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull
        => NoopDisposable.Instance;

    public bool IsEnabled(LogLevel logLevel)
        => logLevel != LogLevel.None && !isDisabled();

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
        {
            return;
        }

        var line = $"{GetLevelPrefix(logLevel)}: {categoryName}";
        if (eventId.Id != 0)
        {
            line += $"[{eventId.Id}]";
        }

        line += $": {message}";

        if (exception is not null)
        {
            _ = messageWriter.TryWrite(line);
            _ = messageWriter.TryWrite(exception.ToString());
            return;
        }

        _ = messageWriter.TryWrite(line);
    }

    private static string GetLevelPrefix(LogLevel logLevel)
        => logLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "none"
        };
}

internal sealed class NoopDisposable : IDisposable
{
    public static NoopDisposable Instance { get; } = new();

    public void Dispose()
    {
    }
}
