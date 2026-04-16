# Aspire Watch Playground

A standalone repro/playground for experimenting with the `Microsoft.DotNet.HotReload.Watch.Aspire` package and Aspire-hosted .NET services.

## What is in here?

- `src/AspireWatchDemo.Starter` — restores the `.slnx` solution, locates the restored Watch.Aspire payload, and launches it against the AppHost.
- `src/AspireWatchDemo.AppHost` — an Aspire AppHost that uses `AddExecutable()` to start the demo services.
- `src/AspireWatchDemo.ApiService` — a small watched ASP.NET Core service on `http://127.0.0.1:5071`.
- `src/AspireWatchDemo.Web` — a second watched ASP.NET Core service on `http://127.0.0.1:5072`.
- `src/AspireWatchDemo.Shared` — shared code used by both services.
- `src/AspireWatchDemo.WatchBootstrap` — shared utility code for locating the Watch.Aspire tool payload and the active SDK.

## Run it

From the repo root:

```shell
dotnet run --project src/AspireWatchDemo.Starter
```

The starter will:

1. run `dotnet restore` against `AspireWatchDemo.slnx`
2. locate `Microsoft.DotNet.HotReload.Watch.Aspire`
3. launch the AppHost through the restored watch binary

You can also point the starter app to local repository with Aspire watch tool:

```shell
dotnet run --project src/AspireWatchDemo.Starter -- --use-private-watch-aspire C:\Users\karolz\code\dotnetsdk\src\Dotnet.Watch\Watch.Aspire\Microsoft.DotNet.HotReload.Watch.Aspire.csproj
```

Do debug the application host you can make it stop and wait for the debugger to attach on startup:

```shell
dotnet run --project src/AspireWatchDemo.Starter -- --wait-for-debugger apphost
```

## What to edit while it is running

### 1. Shared library edit (should affect both services)

Edit:

- `src/AspireWatchDemo.Shared/SharedInfo.cs`

Then refresh:

- `http://127.0.0.1:5071/`
- `http://127.0.0.1:5072/`

### 2. API-only edit (should affect the API service)

Edit:

- `src/AspireWatchDemo.ApiService/Api.cs`

Then refresh:

- `http://127.0.0.1:5071/`

