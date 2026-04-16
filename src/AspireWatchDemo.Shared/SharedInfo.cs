namespace AspireWatchDemo.Shared;

public static class SharedInfo
{
    public const string Message = "Shared message v4 - edit this text again to trigger both services.";

    public static string BuildBanner(string serviceName)
        => $"{serviceName} | pid={Environment.ProcessId} | shared='{Message}'";
}
