# Hosting

The `Oriel.Hosting` package runs an app on the .NET Generic Host. Services
come from dependency injection, logs go to `ILogger`, and the app gets
configuration and hosted services. Apps that need none of this keep
`new OrielApp(...)`, which depends on nothing.

```sh
dotnet add package Oriel.Hosting
```

```csharp
using Oriel;
using Oriel.Hosting;

var builder = OrielApp.CreateBuilder(args);
builder.Options = new AppOptions { Name = "Invoices" };
builder.Services.AddDbContext<Store>();
builder.Services.AddOrielService<Invoices>(OrielServiceScope.Call);
builder.Services.AddOrielService<Notifications>(); // one for the app: its events reach pages

await using var app = builder.Build();
app.OpenWindow(new() { Url = "/" });
return await app.RunAsync();
```

`OrielApp.CreateBuilder` reads the configuration as `Host.CreateApplicationBuilder`
does: `appsettings.json`, `appsettings.{Environment}.json`, environment
variables and the command line.

## Services pages call

`AddOrielService<T>(scope)` adds a `[Service]` class to the container, and
binds it for pages once the app is built. Its constructor gets what it
needs from the container.

| Scope | An instance | For |
|---|---|---|
| `Singleton` (the default) | for the app | services with state, or with events |
| `Window` | per window, disposed with the window's scope when it closes | what a window edits: a document, a selection |
| `Call` | per call, in a scope disposed once the call is done and its result written | EF Core's `DbContext`, units of work |

Notes on the scopes:

- **Events** reach pages only from a `Singleton` service. An instance per
  call or per window has no one to tell, so put events on a service of
  the app and inject it.
- **Results** of a `Call` service are written before its scope is
  disposed. An entity that loads lazily from a `DbContext` can still be
  returned.
- **A registration made first stays.** To build a service yourself, add
  it with a factory before `AddOrielService`:

  ```csharp
  builder.Services.AddSingleton(_ => new Engine(Engine.Locate()));
  builder.Services.AddOrielService<Engine>();
  ```

- **The app.** `OrielApp` is in the container, for services that open
  windows or find them (`FindWindow`).

`ServiceConcurrency` applies as it does without the host. For example,
calls of a `Serial` service run one at a time, whatever their scope.

## Logs

The app's errors (`OrielApp.Error`) and failed calls (`OrielApp.CallFailed`)
go to `ILogger<OrielApp>`, instead of the standard error stream. Logging
is configured as usual: `builder.Logging`, or the `Logging` section of
`appsettings.json`.

## Starting and stopping

`RunAsync` starts the hosted services (`IHostedService`, `BackgroundService`),
runs the app until it quits, then stops them. The host's own shutdown
quits the app: `IHostApplicationLifetime.StopApplication`, or Ctrl+C in a
console. The token given to `RunAsync` quits it too.

`DisposeAsync` disposes the windows' scopes and the container, and with
it the singletons it made. In the chess example, that stops Stockfish.

## Without the host

The host is optional. Its pieces are in Oriel itself:

- `OrielApp.Bind(instance)` binds a service once.
- `OrielApp.Bind<T>(window => new ServiceLease<T>(instance, release))`
  gets an instance for each call, from any container. `release` runs once
  the call is done.
