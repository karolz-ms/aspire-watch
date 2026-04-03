using AspireWatchDemo.Shared;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var startedAt = DateTimeOffset.Now;
const string serviceName = "api-service";
var workingDirectory = Directory.GetCurrentDirectory();

Console.WriteLine($"[{serviceName}] Started at {startedAt:O} (PID {Environment.ProcessId})");
Console.WriteLine($"[{serviceName}] Working directory: {workingDirectory}");
Console.WriteLine($"[{serviceName}] {SharedInfo.BuildBanner(serviceName)}");

app.MapGet("/", () => Results.Json(new
{
    service = serviceName,
    pid = Environment.ProcessId,
    startedAt,
    workingDirectory,
    sharedMessage = SharedInfo.Message,
    hint = "API-only edit picked up by watch."
}));

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName }));
app.MapGet("/shared", () => Results.Text(SharedInfo.BuildBanner(serviceName)));

app.Run();
