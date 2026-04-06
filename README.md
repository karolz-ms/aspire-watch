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

```powershell
dotnet run --project src/AspireWatchDemo.Starter
```

The starter will:

1. run `dotnet restore` against `AspireWatchDemo.slnx`
2. locate `Microsoft.DotNet.HotReload.Watch.Aspire`
3. launch the AppHost through the restored watch binary

## What to edit while it is running

### 1. Shared library edit (should affect both services)

Edit:

- `src/AspireWatchDemo.Shared/SharedInfo.cs`

Then refresh:

- `http://127.0.0.1:5071/`
- `http://127.0.0.1:5072/`

### 2. API-only edit (should affect the API service)

Edit:

- `src/AspireWatchDemo.ApiService/Program.cs`

Then refresh:

- `http://127.0.0.1:5071/`

## Important note about the public 10.0.201 package

This repo is pinned to the public `Microsoft.DotNet.HotReload.Watch.Aspire` `10.0.201` package requested in the task.

That public package currently behaves in **legacy compatibility mode** (`--project`) rather than exposing the newer separate `host` / `server` / `resource` launcher split from the newer SDK PR work.

The code is structured to detect that automatically:

- if the installed Watch.Aspire payload exposes the newer launcher split, the sample is ready to use it
- with the public `10.0.201` payload, it falls back to per-project watch execution so the playground still works today

## Verified locally

The current workspace has already been verified to:

- build successfully from `AspireWatchDemo.slnx` with `.NET SDK 10.0.201`
- start both services successfully
- propagate a shared-library change to both services
- propagate an API-only change only to the API service
