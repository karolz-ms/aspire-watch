using System.Diagnostics;

namespace AspireWatchDemo.WatchBootstrap;

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
