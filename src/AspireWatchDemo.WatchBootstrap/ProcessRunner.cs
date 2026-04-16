using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AspireWatchDemo.WatchBootstrap;

public static class ProcessRunner
{
    private const int CanceledExitCode = 130;
    private const int SigTerm = 15;
    private const uint CtrlCEvent = 0;
    private static readonly TimeSpan GracefulShutdownTimeout = TimeSpan.FromSeconds(10);

    public static async Task<int> RunStreamingAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return CanceledExitCode;
        }

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

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RequestShutdownAsync(process);
            return CanceledExitCode;
        }
    }

    private static async Task RequestShutdownAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        TryRequestGracefulShutdown(process);

        try
        {
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(GracefulShutdownTimeout);
            return;
        }
        catch (TimeoutException)
        {
            // Fall back to forceful termination below.
        }
        catch (InvalidOperationException)
        {
            // The process may exit between the HasExited check and the wait.
            return;
        }

        // Best effort.
        TryTerminateProcess(process);
    }

    private static void TryRequestGracefulShutdown(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                TrySendCtrlC();
                return;
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                SendSigTerm(process.Id);
            }
        }
        catch (InvalidOperationException)
        {
            // The process already exited.
        }
        catch (DllNotFoundException)
        {
            // Signal interop is unavailable on this platform; forceful shutdown remains as fallback.
        }
        catch (EntryPointNotFoundException)
        {
            // Signal interop is unavailable on this platform; forceful shutdown remains as fallback.
        }
    }

    private static void SendSigTerm(int pid)
    {
        _ = kill(pid, SigTerm);
    }

    private static void TryTerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process already exited.
        }
        catch (NotSupportedException)
        {
            // Some platforms may not support terminating the entire process tree.
        }
    }

    private static void TrySendCtrlC()
    {
        _ = SetConsoleCtrlHandler(null, true);

        try
        {
            _ = GenerateConsoleCtrlEvent(CtrlCEvent, 0);
        }
        finally
        {
            _ = SetConsoleCtrlHandler(null, false);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandlerRoutine? handlerRoutine, bool add);

    [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
    private static extern int kill(int pid, int sig);

    private delegate bool ConsoleCtrlHandlerRoutine(uint ctrlType);
}
