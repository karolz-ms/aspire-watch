namespace AspireWatchDemo.WatchBootstrap;

using System.Diagnostics;

public sealed record WatchAspireOptions(string? PrivateBuildProjectPath, IReadOnlySet<string> WaitForDebuggerTargets)
{
    public const string PrivateBuildFlag = "--use-private-watch-aspire";
    public const string WaitForDebuggerFlag = "--wait-for-debugger";
    public const string StarterMoniker = "starter";
    public const string AppHostMoniker = "apphost";

    public bool UsePrivateBuild => !string.IsNullOrWhiteSpace(PrivateBuildProjectPath);

    public bool ShouldWaitForDebugger(string moniker)
        => WaitForDebuggerTargets.Contains(moniker);

    public static WatchAspireOptions FromArguments(IEnumerable<string>? args)
    {
        var arguments = args?.ToArray() ?? [];
        string? privateBuildProjectPath = null;
        var waitForDebuggerTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < arguments.Length; i++)
        {
            var arg = arguments[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (string.Equals(arg, PrivateBuildFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= arguments.Length || IsKnownOption(arguments[i + 1]))
                {
                    throw new ArgumentException($"The '{PrivateBuildFlag}' option requires a path to the private Watch.Aspire project.");
                }

                privateBuildProjectPath = arguments[++i];
                continue;
            }

            if (arg.StartsWith(PrivateBuildFlag + "=", StringComparison.OrdinalIgnoreCase))
            {
                privateBuildProjectPath = arg[(PrivateBuildFlag.Length + 1)..].Trim();
                continue;
            }

            if (string.Equals(arg, WaitForDebuggerFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= arguments.Length || IsKnownOption(arguments[i + 1]))
                {
                    throw new ArgumentException($"The '{WaitForDebuggerFlag}' option requires one or more program monikers.");
                }

                AddDebuggerTargets(arguments[++i], waitForDebuggerTargets);
                continue;
            }

            if (arg.StartsWith(WaitForDebuggerFlag + "=", StringComparison.OrdinalIgnoreCase))
            {
                AddDebuggerTargets(arg[(WaitForDebuggerFlag.Length + 1)..], waitForDebuggerTargets);
            }
        }

        return new(
            string.IsNullOrWhiteSpace(privateBuildProjectPath) ? null : privateBuildProjectPath,
            waitForDebuggerTargets);
    }

    public static string[] FilterApplicationArguments(IEnumerable<string>? args)
    {
        if (args is null)
        {
            return [];
        }

        var arguments = args.ToArray();
        var filtered = new List<string>();

        for (var i = 0; i < arguments.Length; i++)
        {
            var arg = arguments[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (string.Equals(arg, PrivateBuildFlag, StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, WaitForDebuggerFlag, StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            if (arg.StartsWith(PrivateBuildFlag + "=", StringComparison.OrdinalIgnoreCase)
                || arg.StartsWith(WaitForDebuggerFlag + "=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            filtered.Add(arg);
        }

        return [.. filtered];
    }

    public static void WaitForDebugger(CancellationToken cancellationToken)
    {
        while (!Debugger.IsAttached)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(TimeSpan.FromMilliseconds(500));
        }
}

    private static void AddDebuggerTargets(string? rawValue, ISet<string> targets)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            throw new ArgumentException($"The '{WaitForDebuggerFlag}' option requires one or more program monikers.");
        }

        foreach (var value in rawValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!string.Equals(value, StarterMoniker, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, AppHostMoniker, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Unsupported debugger wait moniker '{value}'. Supported values are '{StarterMoniker}' and '{AppHostMoniker}'.");
            }

            targets.Add(value);
        }
    }

    private static bool IsKnownOption(string? arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            return false;
        }

        return string.Equals(arg, PrivateBuildFlag, StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, WaitForDebuggerFlag, StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith(PrivateBuildFlag + "=", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith(WaitForDebuggerFlag + "=", StringComparison.OrdinalIgnoreCase);
    }
}
