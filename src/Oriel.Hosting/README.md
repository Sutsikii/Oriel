# Oriel.Hosting

Runs an [Oriel](https://github.com/Sutsikii/Oriel) app on the .NET Generic
Host: services from dependency injection, per call or per window, logs to
`ILogger`, configuration and hosted services.

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

`AddOrielService<T>(scope)` adds a `[Service]` class that pages call, with
an instance for the app (`Singleton`, the default), per window (`Window`)
or per call (`Call`, for a `DbContext`). `RunAsync` starts the hosted
services, runs the app until it quits, then stops them.

The guide: [docs/hosting.md](https://github.com/Sutsikii/Oriel/blob/main/docs/hosting.md).
