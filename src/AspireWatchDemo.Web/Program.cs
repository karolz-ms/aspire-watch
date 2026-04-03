using AspireWatchDemo.Shared;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var startedAt = DateTimeOffset.Now;
const string serviceName = "web-service";
var workingDirectory = Directory.GetCurrentDirectory();

Console.WriteLine($"[{serviceName}] Started at {startedAt:O} (PID {Environment.ProcessId})");
Console.WriteLine($"[{serviceName}] Working directory: {workingDirectory}");
Console.WriteLine($"[{serviceName}] {SharedInfo.BuildBanner(serviceName)}");

app.MapGet("/", () => Results.Content($"""
<!DOCTYPE html>
<html lang=\"en\">
<head><meta charset=\"utf-8\"><title>Aspire Watch Demo</title></head>
<body>
  <h1>Aspire Watch Demo</h1>
  <p><strong>Service:</strong> {serviceName}</p>
  <p><strong>PID:</strong> {Environment.ProcessId}</p>
  <p><strong>Started:</strong> {startedAt:O}</p>
  <p><strong>Working directory:</strong> {workingDirectory}</p>
  <p><strong>Shared message:</strong> {SharedInfo.Message}</p>
  <p>Edit <code>src/AspireWatchDemo.Shared/Class1.cs</code> or this file while the playground is running.</p>
</body>
</html>
""", "text/html"));

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName }));
app.MapGet("/shared", () => Results.Text(SharedInfo.BuildBanner(serviceName)));

app.Run();
