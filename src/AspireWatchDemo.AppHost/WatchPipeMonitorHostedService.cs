using System.IO.Pipes;
using AspireWatchDemo.WatchBootstrap;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal sealed class WatchPipeMonitorHostedService(
    WatchPipeNames pipeNames,
    ILogger<WatchPipeMonitorHostedService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(
            ListenForStatusEventsAsync(stoppingToken),
            HoldControlPipeOpenAsync(stoppingToken));

    private async Task ListenForStatusEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var statusPipe = new NamedPipeServerStream(
                pipeNames.StatusPipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            logger.LogInformation("Waiting for Watch.Aspire status connection on pipe '{PipeName}'.", pipeNames.StatusPipeName);
            await statusPipe.WaitForConnectionAsync(cancellationToken);
            logger.LogInformation("Watch.Aspire status pipe connected.");

            using var reader = new StreamReader(statusPipe);
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    logger.LogInformation("[watch-status] {Line}", line);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task HoldControlPipeOpenAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var controlPipe = new NamedPipeServerStream(
                pipeNames.ControlPipeName,
                PipeDirection.Out,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            logger.LogInformation("Waiting for Watch.Aspire control connection on pipe '{PipeName}'.", pipeNames.ControlPipeName);
            await controlPipe.WaitForConnectionAsync(cancellationToken);
            logger.LogInformation("Watch.Aspire control pipe connected. Manual rebuild commands are not sent automatically in this sample.");

            using var writer = new StreamWriter(controlPipe) { AutoFlush = true };
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
