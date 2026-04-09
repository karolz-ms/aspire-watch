using System.Security.Cryptography;

namespace AspireWatchDemo.WatchBootstrap;

public sealed record WatchPipeNames(string ServerPipeName, string StatusPipeName, string ControlPipeName);

public static class PipeNameFactory
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    public static WatchPipeNames CreateSet(int suffixLength = 6)
    {
        var suffix = CreateRandomSuffix(suffixLength);
        return new(
            ServerPipeName: $"server-{suffix}",
            StatusPipeName: $"status-{suffix}",
            ControlPipeName: $"control-{suffix}");
    }

    public static string CreateName(string prefix, int suffixLength = 6)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        return $"{prefix}-{CreateRandomSuffix(suffixLength)}";
    }

    private static string CreateRandomSuffix(int length = 6)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        Span<char> buffer = stackalloc char[length];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(buffer);
    }
}
