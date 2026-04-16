namespace AspireWatchDemo.WatchBootstrap;

public sealed record WatchAspireOptions(string? PrivateBuildProjectPath)
{
    public const string PrivateBuildFlag = "--use-private-watch-aspire";

    public bool UsePrivateBuild => !string.IsNullOrWhiteSpace(PrivateBuildProjectPath);

    public static WatchAspireOptions FromArguments(IEnumerable<string>? args)
    {
        var arguments = args?.ToArray() ?? [];
        string? privateBuildProjectPath = null;

        for (var i = 0; i < arguments.Length; i++)
        {
            var arg = arguments[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (string.Equals(arg, PrivateBuildFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= arguments.Length || IsPrivateBuildFlag(arguments[i + 1]))
                {
                    throw new ArgumentException($"The '{PrivateBuildFlag}' option requires a path to the private Watch.Aspire project.");
                }

                privateBuildProjectPath = arguments[++i];
                continue;
            }

            if (arg.StartsWith(PrivateBuildFlag + "=", StringComparison.OrdinalIgnoreCase))
            {
                privateBuildProjectPath = arg[(PrivateBuildFlag.Length + 1)..].Trim();
            }
        }

        return new(string.IsNullOrWhiteSpace(privateBuildProjectPath) ? null : privateBuildProjectPath);
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

            if (string.Equals(arg, PrivateBuildFlag, StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            if (arg.StartsWith(PrivateBuildFlag + "=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            filtered.Add(arg);
        }

        return [.. filtered];
    }

    private static bool IsPrivateBuildFlag(string? arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            return false;
        }

        return string.Equals(arg, PrivateBuildFlag, StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith(PrivateBuildFlag + "=", StringComparison.OrdinalIgnoreCase);
    }
}
